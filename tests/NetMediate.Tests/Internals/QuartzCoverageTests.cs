using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NetMediate.Quartz;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Spi;
using System.Collections.Specialized;
using System.Reflection;

namespace NetMediate.Tests.Internals;

public sealed class QuartzCoverageTests
{
    private sealed record QuartzMessage(string Value);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<(LogLevel LogLevel, string Message)> Entries { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(Entries);

        public void Dispose() { }

        private sealed class CapturingLogger(List<(LogLevel LogLevel, string Message)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => NullScope.Instance;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                entries.Add((logLevel, formatter(state, exception)));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose() { }
            }
        }
    }

    private sealed class CapturingNotifiable : INotifiable
    {
        private readonly TaskCompletionSource<(object? Key, object Message)> _notificationSource = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public Task DispatchNotifications<TMessage>(
            object? key,
            TMessage message,
            INotificationHandler<TMessage>[] handlers,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull
        {
            _notificationSource.TrySetResult((key, message));
            return Task.CompletedTask;
        }

        public async Task<(object? Key, TMessage Message)> WaitAsync<TMessage>()
            where TMessage : notnull
        {
            var (key, message) = await _notificationSource.Task.WaitAsync(TestContext.Current.CancellationToken);
            return (key, Assert.IsType<TMessage>(message));
        }
    }

    private sealed class TrackingHandler<TMessage> : INotificationHandler<TMessage>
        where TMessage : notnull
    {
        public int CallCount { get; private set; }

        public Task Handle(TMessage message, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class TestNotifiable : INotifiable
    {
        public Task DispatchNotifications<TMessage>(
            object? key,
            TMessage message,
            INotificationHandler<TMessage>[] handlers,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull => Task.CompletedTask;
    }

    private sealed class ServiceProviderJobFactory(IServiceProvider serviceProvider) : IJobFactory
    {
        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler) =>
            serviceProvider
                .GetServices<IJob>()
                .Single(job => job.GetType() == bundle.JobDetail.JobType);

        public void ReturnJob(IJob job) { }
    }

    private static Task<IScheduler> CreateSchedulerAsync()
    {
        var properties = new NameValueCollection
        {
            ["quartz.scheduler.instanceName"] = $"NetMediateTests-{Guid.NewGuid():N}",
            ["quartz.scheduler.instanceId"] = Guid.NewGuid().ToString("N"),
            ["quartz.threadPool.threadCount"] = "1",
        };

        return new StdSchedulerFactory(properties).GetScheduler(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AddNetMediateQuartz_ConfiguresQuartzNotifierAndOptions()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz(options => options.GroupName = "custom-group");

        using var provider = services.BuildServiceProvider();

        var notifiable = provider.GetRequiredService<INotifiable>();
        var options = provider.GetRequiredService<IOptions<QuartzNotificationOptions>>();

        Assert.IsType<QuartzNotifier>(notifiable);
        Assert.Equal("custom-group", options.Value.GroupName);
    }

    [Fact]
    public async Task AddNetMediateQuartz_PreservesKeyedServiceKeyAndImplementationInstance()
    {
        var scheduler = await CreateSchedulerAsync();
        var innerMediator = global::Moq.Mock.Of<IMediator>();
        var innerNotifiable = new CapturingNotifiable();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddKeyedSingleton<IMediator>("mediator-key", innerMediator);
        services.AddKeyedSingleton<INotifiable>("notifier-key", innerNotifiable);
        services.AddNetMediateQuartz();

        using var provider = services.BuildServiceProvider();

        var mediator = Assert.IsType<QuartzMediator>(
            provider.GetRequiredKeyedService<IMediator>("mediator-key")
        );
        var notifiable = Assert.IsType<QuartzNotifier>(
            provider.GetRequiredKeyedService<INotifiable>("notifier-key")
        );

        Assert.Same(innerMediator, mediator.Inner);
        Assert.Same(innerNotifiable, notifiable.Inner);
    }

    [Fact]
    public async Task QuartzNotifier_Notify_SchedulesQuartzJobWithSerializedRoutingKey()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz(options => options.GroupName = "coverage-tests");

        using var provider = services.BuildServiceProvider();

        var notifier = Assert.IsType<QuartzMediator>(provider.GetRequiredService<IMediator>());
        var serializer = provider.GetRequiredService<INotificationSerializer>();

        await notifier.Notify("orders", new QuartzMessage("created"), TestContext.Current.CancellationToken);

        var jobKey = Assert.Single(
            await scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("coverage-tests"), TestContext.Current.CancellationToken)
        );
        var job = await scheduler.GetJobDetail(jobKey, TestContext.Current.CancellationToken);
        Assert.NotNull(job);

        Assert.Equal(
            serializer.Serialize(new QuartzMessage("created")),
            job.JobDataMap.GetString(QuartzNotificationJob.MessageDataKey)
        );
        Assert.Equal(typeof(QuartzMessage).AssemblyQualifiedName, job.JobDataMap.GetString(QuartzNotificationJob.TypeDataKey));
        Assert.Equal("\"orders\"", job.JobDataMap.GetString(QuartzNotificationJob.KeyDataKey));
        Assert.Equal(typeof(string).AssemblyQualifiedName, job.JobDataMap.GetString(QuartzNotificationJob.KeyTypeDataKey));
    }

    [Fact]
    public async Task QuartzNotificationJob_Execute_DispatchesStoredNotificationThroughResolvedINotifiable()
    {
        var capture = new CapturingNotifiable();
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz();
        services.AddSingleton<INotifiable>(capture);

        using var provider = services.BuildServiceProvider();
        scheduler.JobFactory = new ServiceProviderJobFactory(provider);

        await scheduler.Start(TestContext.Current.CancellationToken);

        try
        {
            var serializer = provider.GetRequiredService<INotificationSerializer>();
            var job = JobBuilder.Create<QuartzNotificationJob>()
                .WithIdentity("dispatch", "coverage")
                .UsingJobData(QuartzNotificationJob.MessageDataKey, serializer.Serialize(new QuartzMessage("queued")))
                .UsingJobData(QuartzNotificationJob.TypeDataKey, typeof(QuartzMessage).AssemblyQualifiedName!)
                .UsingJobData(QuartzNotificationJob.KeyDataKey, "\"orders\"")
                .UsingJobData(QuartzNotificationJob.KeyTypeDataKey, typeof(string).AssemblyQualifiedName!)
                .Build();

            var trigger = TriggerBuilder.Create()
                .WithIdentity("dispatch-trigger", "coverage")
                .StartNow()
                .Build();

            await scheduler.ScheduleJob(job, trigger, TestContext.Current.CancellationToken);

            var (key, message) = await capture.WaitAsync<QuartzMessage>();

            Assert.Equal("orders", key);
            Assert.Equal("queued", message.Value);
        }
        finally
        {
            await scheduler.Clear(TestContext.Current.CancellationToken);
            await scheduler.Shutdown(waitForJobsToComplete: true, cancellationToken: TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task QuartzNotificationJob_DispatchNotification_WithNullKey_UsesUnkeyedHandlers()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz();
        services.AddSingleton<INotificationHandler<QuartzMessage>, TrackingHandler<QuartzMessage>>();

        using var provider = services.BuildServiceProvider();

        await QuartzNotificationJob.DispatchNotification<QuartzMessage>(
            provider,
            key: null,
            message: new QuartzMessage("unkeyed"),
            cancellationToken: TestContext.Current.CancellationToken
        );
    }

    [Fact]
    public void NetMediateQuartzDI_CreateServiceInstance_CoversFactoryInstanceTypeAndInvalidDescriptors()
    {
        var method = typeof(NetMediateQuartzDI)
            .GetMethod(
                "CreateServiceInstance",
                BindingFlags.NonPublic | BindingFlags.Static
            )!
            .MakeGenericMethod(typeof(INotifiable));

        var serviceProvider = new ServiceCollection().BuildServiceProvider();
        var directInstance = new TestNotifiable();

        var fromFactory = (INotifiable)method.Invoke(
            null,
            [
                ServiceDescriptor.Singleton<INotifiable>(_ => new TestNotifiable()),
                serviceProvider,
                null
            ]
        )!;
        Assert.NotNull(fromFactory);

        var fromInstance = (INotifiable)method.Invoke(
            null,
            [
                ServiceDescriptor.Singleton<INotifiable>(directInstance),
                serviceProvider,
                null
            ]
        )!;
        Assert.Same(directInstance, fromInstance);

        var fromType = (INotifiable)method.Invoke(
            null,
            [
                ServiceDescriptor.Singleton<INotifiable, TestNotifiable>(),
                serviceProvider,
                null
            ]
        )!;
        Assert.IsType<TestNotifiable>(fromType);

        var keyedServices = new ServiceCollection();
        keyedServices.AddKeyedSingleton<INotifiable>("k-factory", (_, _) => new TestNotifiable());
        keyedServices.AddKeyedSingleton<INotifiable>("k-instance", directInstance);
        keyedServices.AddKeyedSingleton<INotifiable, TestNotifiable>("k-type");
        var descriptors = keyedServices.ToArray();
        var keyedProvider = keyedServices.BuildServiceProvider();

        Assert.NotNull(method.Invoke(null, [descriptors[0], keyedProvider, "k-factory"]));
        Assert.Same(directInstance, method.Invoke(null, [descriptors[1], keyedProvider, "k-instance"]));
        Assert.IsType<TestNotifiable>(method.Invoke(null, [descriptors[2], keyedProvider, "k-type"]));
    }

    [Fact]
    public async Task QuartzNotifier_DispatchNotifications_WithNoHandlers_Completes()
    {
        var scheduler = await CreateSchedulerAsync();
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(loggerProvider);
        });
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz();

        using var provider = services.BuildServiceProvider();
        var notifier = Assert.IsType<QuartzNotifier>(provider.GetRequiredService<INotifiable>());

        await notifier.DispatchNotifications<object>(
            null,
            new object(),
            [],
            TestContext.Current.CancellationToken
        );

        var entry = Assert.Single(loggerProvider.Entries);
        Assert.Equal(LogLevel.Debug, entry.LogLevel);
        Assert.Contains("no handlers registered", entry.Message);
    }

    [Fact]
    public async Task QuartzNotifier_DispatchNotifications_WithHandlers_InvokesEachHandler()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz();

        using var provider = services.BuildServiceProvider();
        var notifier = Assert.IsType<QuartzNotifier>(provider.GetRequiredService<INotifiable>());
        var first = new TrackingHandler<QuartzMessage>();
        var second = new TrackingHandler<QuartzMessage>();

        await notifier.DispatchNotifications(
            null,
            new QuartzMessage("handled"),
            [first, second],
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task QuartzNotifier_NotifyBatch_SchedulesEachMessage()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz(options => options.GroupName = "batch-tests");

        using var provider = services.BuildServiceProvider();
        var notifier = Assert.IsType<QuartzMediator>(provider.GetRequiredService<IMediator>());

        await notifier.Notify(
            null,
            [new QuartzMessage("one"), new QuartzMessage("two")],
            TestContext.Current.CancellationToken
        );

        var jobKeys = await scheduler.GetJobKeys(
            GroupMatcher<JobKey>.GroupEquals("batch-tests"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, jobKeys.Count);
    }

    [Fact]
    public async Task QuartzNotifier_NotifyBatch_WithEmptyMessages_CompletesWithoutScheduling()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz(options => options.GroupName = "empty-batch-tests");

        using var provider = services.BuildServiceProvider();
        var notifier = Assert.IsType<QuartzMediator>(provider.GetRequiredService<IMediator>());

        await scheduler.Clear(TestContext.Current.CancellationToken);

        await notifier.Notify(
            null,
            (IEnumerable<QuartzMessage>)[],
            TestContext.Current.CancellationToken
        );

        var jobKeys = await scheduler.GetJobKeys(
            GroupMatcher<JobKey>.GroupEquals("empty-batch-tests"),
            TestContext.Current.CancellationToken
        );

        Assert.Empty(jobKeys);
    }

    [Fact]
    public async Task QuartzMediator_NotifyWithoutKey_DelegatesToKeyedOverload()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz(options => options.GroupName = "notify-no-key-tests");

        using var provider = services.BuildServiceProvider();
        var mediator = Assert.IsType<QuartzMediator>(provider.GetRequiredService<IMediator>());

        await mediator.Notify(new QuartzMessage("single"), TestContext.Current.CancellationToken);

        var jobKeys = await scheduler.GetJobKeys(
            GroupMatcher<JobKey>.GroupEquals("notify-no-key-tests"),
            TestContext.Current.CancellationToken
        );

        var jobKey = Assert.Single(jobKeys);
        var job = await scheduler.GetJobDetail(jobKey, TestContext.Current.CancellationToken);
        Assert.NotNull(job);
        Assert.False(job.JobDataMap.ContainsKey(QuartzNotificationJob.KeyDataKey));
    }

    [Fact]
    public async Task QuartzMediator_NotifyBatchWithoutKey_DelegatesToKeyedOverload()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz(options => options.GroupName = "batch-no-key-tests");

        using var provider = services.BuildServiceProvider();
        var mediator = Assert.IsType<QuartzMediator>(provider.GetRequiredService<IMediator>());

        await mediator.Notify(
            (IEnumerable<QuartzMessage>)[new QuartzMessage("one"), new QuartzMessage("two")],
            TestContext.Current.CancellationToken
        );

        var jobKeys = await scheduler.GetJobKeys(
            GroupMatcher<JobKey>.GroupEquals("batch-no-key-tests"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, jobKeys.Count);
    }

    [Fact]
    public async Task QuartzMediator_RequestSendAndStreamMembers_ForwardToInnerMediator()
    {
        var requestTask = Task.FromResult(7);
        var streamResult = YieldIntegers(1, 2);
        var inner = new global::Moq.Mock<IMediator>(global::Moq.MockBehavior.Strict);

        inner.Setup(m => m.Request<QuartzMessage, int>(
                global::Moq.It.IsAny<QuartzMessage>(),
                global::Moq.It.IsAny<CancellationToken>()))
            .Returns(requestTask);
        inner.Setup(m => m.Request<QuartzMessage, int>(
                global::Moq.It.IsAny<object?>(),
                global::Moq.It.IsAny<QuartzMessage>(),
                global::Moq.It.IsAny<CancellationToken>()))
            .Returns(requestTask);
        inner.Setup(m => m.RequestStream<QuartzMessage, int>(
                global::Moq.It.IsAny<QuartzMessage>(),
                global::Moq.It.IsAny<CancellationToken>()))
            .Returns(streamResult);
        inner.Setup(m => m.RequestStream<QuartzMessage, int>(
                global::Moq.It.IsAny<object?>(),
                global::Moq.It.IsAny<QuartzMessage>(),
                global::Moq.It.IsAny<CancellationToken>()))
            .Returns(streamResult);
        inner.Setup(m => m.Send(
                global::Moq.It.IsAny<QuartzMessage>(),
                global::Moq.It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        inner.Setup(m => m.Send(
                global::Moq.It.IsAny<object?>(),
                global::Moq.It.IsAny<QuartzMessage>(),
                global::Moq.It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        inner.Setup(m => m.Send(
                global::Moq.It.IsAny<IEnumerable<QuartzMessage>>(),
                global::Moq.It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        inner.Setup(m => m.Send(
                global::Moq.It.IsAny<object?>(),
                global::Moq.It.IsAny<IEnumerable<QuartzMessage>>(),
                global::Moq.It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var mediator = new QuartzMediator
        {
            Inner = inner.Object,
            Logger = NullLogger<QuartzMediator>.Instance,
            Options = Options.Create(new QuartzNotificationOptions()),
            Scheduler = await CreateSchedulerAsync(),
            Serializer = new JsonNotificationSerializer(),
        };

        var req = new QuartzMessage("request");
        Assert.Equal(7, await mediator.Request<QuartzMessage, int>(req, TestContext.Current.CancellationToken));
        Assert.Equal(7, await mediator.Request<QuartzMessage, int>("k", req, TestContext.Current.CancellationToken));

        var streamOne = await mediator.RequestStream<QuartzMessage, int>(req, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        var streamTwo = await mediator.RequestStream<QuartzMessage, int>("k", req, TestContext.Current.CancellationToken).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal([1, 2], streamOne);
        Assert.Equal([1, 2], streamTwo);

        await mediator.Send(req, TestContext.Current.CancellationToken);
        await mediator.Send("k", req, TestContext.Current.CancellationToken);
        await mediator.Send((IEnumerable<QuartzMessage>)[req], TestContext.Current.CancellationToken);
        await mediator.Send("k", (IEnumerable<QuartzMessage>)[req], TestContext.Current.CancellationToken);

        inner.VerifyAll();
    }

    [Fact]
    public async Task QuartzNotifier_Notify_WhenDebugEnabled_LogsScheduledJob()
    {
        var scheduler = await CreateSchedulerAsync();
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(loggerProvider);
        });
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz(options => options.GroupName = "log-tests");

        using var provider = services.BuildServiceProvider();
        var notifier = Assert.IsType<QuartzMediator>(provider.GetRequiredService<IMediator>());

        await notifier.Notify(null, new QuartzMessage("logged"), TestContext.Current.CancellationToken);

        var entry = Assert.Single(loggerProvider.Entries);
        Assert.Equal(LogLevel.Debug, entry.LogLevel);
        Assert.Contains("scheduled notification job", entry.Message);
        Assert.Contains(nameof(QuartzMessage), entry.Message);
    }

    [Fact]
    public async Task QuartzNotificationJob_Execute_WithMissingMessageData_Returns()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(loggerProvider);
        });
        services.AddNetMediateQuartz();

        using var provider = services.BuildServiceProvider();
        var job = provider.GetServices<IJob>().OfType<QuartzNotificationJob>().Single();

        var detail = JobBuilder.Create<QuartzNotificationJob>()
            .WithIdentity("missing", "coverage")
            .UsingJobData(QuartzNotificationJob.MessageDataKey, string.Empty)
            .UsingJobData(QuartzNotificationJob.TypeDataKey, string.Empty)
            .Build();

        await job.Execute(CreateJobContext(detail));

        var entry = Assert.Single(loggerProvider.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Contains("missing message data", entry.Message);
    }

    [Fact]
    public async Task QuartzNotificationJob_Execute_WithUnknownType_Returns()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(loggerProvider);
        });
        services.AddNetMediateQuartz();

        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<INotificationSerializer>();
        var job = provider.GetServices<IJob>().OfType<QuartzNotificationJob>().Single();

        var detail = JobBuilder.Create<QuartzNotificationJob>()
            .WithIdentity("unknown-type", "coverage")
            .UsingJobData(QuartzNotificationJob.MessageDataKey, serializer.Serialize(new QuartzMessage("queued")))
            .UsingJobData(QuartzNotificationJob.TypeDataKey, "Unknown.Type, Missing.Assembly")
            .Build();

        await job.Execute(CreateJobContext(detail));

        var entry = Assert.Single(loggerProvider.Entries);
        Assert.Equal(LogLevel.Error, entry.LogLevel);
        Assert.Contains("cannot resolve type", entry.Message);
    }

    [Fact]
    public async Task QuartzNotificationJob_Execute_WithNullDeserializedMessage_Returns()
    {
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddOptions();
        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Debug);
            builder.AddProvider(loggerProvider);
        });
        services.AddNetMediateQuartz();
        services.AddSingleton<INotificationSerializer, NullSerializer>();

        using var provider = services.BuildServiceProvider();
        var job = provider.GetServices<IJob>().OfType<QuartzNotificationJob>().Single();

        var detail = JobBuilder.Create<QuartzNotificationJob>()
            .WithIdentity("null-message", "coverage")
            .UsingJobData(QuartzNotificationJob.MessageDataKey, "{}")
            .UsingJobData(QuartzNotificationJob.TypeDataKey, typeof(QuartzMessage).AssemblyQualifiedName!)
            .UsingJobData(QuartzNotificationJob.KeyDataKey, string.Empty)
            .UsingJobData(QuartzNotificationJob.KeyTypeDataKey, string.Empty)
            .Build();

        await job.Execute(CreateJobContext(detail));

        var entry = Assert.Single(loggerProvider.Entries);
        Assert.Equal(LogLevel.Warning, entry.LogLevel);
        Assert.Contains("deserialized message is null", entry.Message);
    }

    private static IJobExecutionContext CreateJobContext(IJobDetail jobDetail)
    {
        var context = new global::Moq.Mock<IJobExecutionContext>();
        context.SetupGet(x => x.JobDetail).Returns(jobDetail);
        context.SetupGet(x => x.CancellationToken).Returns(TestContext.Current.CancellationToken);
        return context.Object;
    }

    private sealed class NullSerializer : INotificationSerializer
    {
        public string Serialize<TMessage>(TMessage message)
            where TMessage : notnull => "{}";

        public object? Deserialize(string data, Type messageType) => null;
    }

    private static async IAsyncEnumerable<int> YieldIntegers(params int[] values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return value;
        }
    }
}
