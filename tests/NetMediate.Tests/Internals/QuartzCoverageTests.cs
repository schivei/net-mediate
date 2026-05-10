using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NetMediate.Quartz;
using Quartz;
using Quartz.Impl;
using Quartz.Impl.Matchers;
using Quartz.Spi;

namespace NetMediate.Tests.Internals;

public sealed class QuartzCoverageTests
{
    private sealed record QuartzMessage(string Value);

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
            where TMessage : notnull => Task.CompletedTask;

        public Task Notify<TMessage>(
            object? key,
            TMessage message,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull
        {
            _notificationSource.TrySetResult((key, message!));
            return Task.CompletedTask;
        }

        public async Task Notify<TMessage>(
            object? key,
            IEnumerable<TMessage> messages,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull
        {
            foreach (var message in messages)
                await Notify(key, message, cancellationToken);
        }

        public async Task<(object? Key, TMessage Message)> WaitAsync<TMessage>()
            where TMessage : notnull
        {
            var (key, message) = await _notificationSource.Task.WaitAsync(TestContext.Current.CancellationToken);
            return (key, Assert.IsType<TMessage>(message));
        }
    }

    private sealed class ServiceProviderJobFactory(IServiceProvider serviceProvider) : IJobFactory
    {
        public IJob NewJob(TriggerFiredBundle bundle, IScheduler scheduler) =>
            serviceProvider
                .GetServices<IJob>()
                .Single(job => job.GetType() == bundle.JobDetail.JobType);

        public void ReturnJob(IJob job) { }
    }

    private static Task<IScheduler> CreateSchedulerAsync() =>
        new StdSchedulerFactory().GetScheduler(TestContext.Current.CancellationToken);

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
    public async Task QuartzNotifier_Notify_SchedulesQuartzJobWithSerializedRoutingKey()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
        services.AddSingleton(scheduler);
        services.AddNetMediateQuartz(options => options.GroupName = "coverage-tests");

        using var provider = services.BuildServiceProvider();

        var notifier = Assert.IsType<QuartzNotifier>(provider.GetRequiredService<INotifiable>());
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
    public async Task QuartzNotifier_DispatchNotifications_WithNoHandlers_Completes()
    {
        var scheduler = await CreateSchedulerAsync();
        var services = new ServiceCollection();

        services.AddOptions();
        services.AddLogging();
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
    }
}
