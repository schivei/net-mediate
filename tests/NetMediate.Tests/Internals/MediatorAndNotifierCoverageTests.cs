using System.Runtime.CompilerServices;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public sealed class MediatorAndNotifierCoverageTests
{
    private sealed record NotificationMessage(string Value);
    private sealed record CommandMessage(int Value);
    private sealed record RequestMessage(int Value);
    private sealed record StreamMessage(int Value);
    private sealed record Response(int Value);

    [Fact]
    public async Task Mediator_Notify_DelegatesSingleAndBatchMessages()
    {
        var notifier = new SpyNotifiable();
        var mediator = new Mediator(new ServiceCollection().BuildServiceProvider(), notifier);

        await mediator.Notify(new NotificationMessage("one"), TestContext.Current.CancellationToken);
        await mediator.Notify(
            [new NotificationMessage("batch-one"), new NotificationMessage("batch-two")],
            TestContext.Current.CancellationToken
        );
        await mediator.Notify(
            "key",
            [new NotificationMessage("two"), new NotificationMessage("three")],
            TestContext.Current.CancellationToken
        );

        var singleCall = Assert.Single(notifier.SingleCalls);
        Assert.Null(singleCall.Key);
        Assert.Equal("one", singleCall.Message.Value);
        Assert.Collection(
            notifier.BatchCalls,
            call =>
            {
                Assert.Null(call.Key);
                Assert.Equal(
                    ["batch-one", "batch-two"],
                    [.. call.Messages.Select(message => message.Value)]
                );
            },
            call =>
            {
                Assert.Equal("key", call.Key);
                Assert.Equal(["two", "three"], [.. call.Messages.Select(message => message.Value)]);
            }
        );
    }

    [Fact]
    public async Task Mediator_Send_UsesPipelineForSingleAndBatchMessages()
    {
        var handler = new RecordingCommandHandler();
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<ICommandHandler<CommandMessage>>(handler);
            services.AddSingleton(sp =>
                new CommandPipelineExecutor<CommandMessage>(
                    sp,
                    NullLogger<CommandPipelineExecutor<CommandMessage>>.Instance
                )
            );
        });

        var mediator = new Mediator(provider, new SpyNotifiable());

        await mediator.Send(
            [new CommandMessage(1), new CommandMessage(2)],
            TestContext.Current.CancellationToken
        );

        Assert.Equal([1, 2], handler.Values);
    }

    [Fact]
    public async Task Mediator_Send_WhenPipelineThrows_WrapsException()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<ICommandHandler<CommandMessage>, RecordingCommandHandler>();
            services.AddSingleton<IPipelineCommandBehavior<CommandMessage>, ThrowingCommandBehavior>();
            services.AddSingleton(sp =>
                new CommandPipelineExecutor<CommandMessage>(
                    sp,
                    NullLogger<CommandPipelineExecutor<CommandMessage>>.Instance
                )
            );
        });

        var mediator = new Mediator(provider, new SpyNotifiable());
        var exception = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.Send(new CommandMessage(1), TestContext.Current.CancellationToken)
        );

        Assert.Equal(typeof(CommandMessage), exception.MessageType);
        Assert.Equal(typeof(ICommandHandler<CommandMessage>), exception.HandlerType);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Mediator_Send_WhenPipelineThrowsWithCurrentActivity_CapturesTraceId()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<ICommandHandler<CommandMessage>, RecordingCommandHandler>();
            services.AddSingleton<IPipelineCommandBehavior<CommandMessage>, ThrowingCommandBehavior>();
            services.AddSingleton(sp =>
                new CommandPipelineExecutor<CommandMessage>(
                    sp,
                    NullLogger<CommandPipelineExecutor<CommandMessage>>.Instance
                )
            );
        });

        using var activity = new Activity("send").Start();
        var mediator = new Mediator(provider, new SpyNotifiable());

        var exception = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.Send(new CommandMessage(10), TestContext.Current.CancellationToken)
        );

        Assert.Equal(activity.Id, exception.TraceId);
    }

    [Fact]
    public async Task Mediator_Send_WhenPipelineThrowsMediatorException_RethrowsSameInstance()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<ICommandHandler<CommandMessage>, RecordingCommandHandler>();
            services.AddSingleton<IPipelineCommandBehavior<CommandMessage>, ThrowingMediatorExceptionCommandBehavior>();
            services.AddSingleton(sp =>
                new CommandPipelineExecutor<CommandMessage>(
                    sp,
                    NullLogger<CommandPipelineExecutor<CommandMessage>>.Instance
                )
            );
        });

        var mediator = new Mediator(provider, new SpyNotifiable());

        var exception = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.Send(new CommandMessage(1), TestContext.Current.CancellationToken)
        );

        Assert.Equal("trace-id", exception.TraceId);
    }

    [Fact]
    public async Task Mediator_Request_WhenExecutorCompletes_ReturnsResponse()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IRequestHandler<RequestMessage, Response>, RecordingRequestHandler>();
            services.AddSingleton(sp =>
                new RequestPipelineExecutor<RequestMessage, Response>(
                    sp,
                    NullLogger<RequestPipelineExecutor<RequestMessage, Response>>.Instance
                )
            );
        });

        var mediator = new Mediator(provider, new SpyNotifiable());
        var response = await mediator.Request<RequestMessage, Response>(
            new RequestMessage(7),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(7, response.Value);
    }

    [Fact]
    public async Task Mediator_Request_WhenPipelineConstructionFails_WrapsException()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton(sp =>
                new RequestPipelineExecutor<RequestMessage, Response>(
                    sp,
                    NullLogger<RequestPipelineExecutor<RequestMessage, Response>>.Instance
                )
            );
        });

        var mediator = new Mediator(provider, new SpyNotifiable());

        var exception = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.Request<RequestMessage, Response>(
                new RequestMessage(9),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(typeof(RequestMessage), exception.MessageType);
        Assert.Equal(typeof(IRequestHandler<RequestMessage, Response>), exception.HandlerType);
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Mediator_Request_WhenPipelineThrowsWithCurrentActivity_CapturesTraceId()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IRequestHandler<RequestMessage, Response>, RecordingRequestHandler>();
            services.AddSingleton<IPipelineRequestBehavior<RequestMessage, Response>, ThrowingRequestBehavior>();
            services.AddSingleton(sp =>
                new RequestPipelineExecutor<RequestMessage, Response>(
                    sp,
                    NullLogger<RequestPipelineExecutor<RequestMessage, Response>>.Instance
                )
            );
        });

        using var activity = new Activity("request").Start();
        var mediator = new Mediator(provider, new SpyNotifiable());

        var exception = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.Request<RequestMessage, Response>(
                new RequestMessage(11),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(activity.Id, exception.TraceId);
    }

    [Fact]
    public async Task Mediator_RequestStream_ConcatenatesHandlerStreams()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IStreamHandler<StreamMessage, int>>(
                new RecordingStreamHandler(multiplier: 1)
            );
            services.AddSingleton<IStreamHandler<StreamMessage, int>>(
                new RecordingStreamHandler(multiplier: 10)
            );
            services.AddSingleton(sp => new StreamPipelineExecutor<StreamMessage, int>(sp));
        });

        var mediator = new Mediator(provider, new SpyNotifiable());
        var items = await ToList(
            mediator.RequestStream<StreamMessage, int>(
                new StreamMessage(3),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal([3, 4, 30, 40], items);
    }

    [Fact]
    public async Task Mediator_RequestStream_WithSingleHandler_ReturnsSingleStream()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IStreamHandler<StreamMessage, int>>(
                new RecordingStreamHandler(multiplier: 2)
            );
            services.AddSingleton(sp => new StreamPipelineExecutor<StreamMessage, int>(sp));
        });

        var mediator = new Mediator(provider, new SpyNotifiable());
        var items = await ToList(
            mediator.RequestStream<StreamMessage, int>(
                new StreamMessage(3),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal([6, 8], items);
    }

    [Fact]
    public async Task Notifier_DispatchNotifications_InvokesAllHandlers()
    {
        var notifier = new Notifier(new ServiceCollection().BuildServiceProvider());
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await notifier.DispatchNotifications(
            null,
            new NotificationMessage("value"),
            [
                new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                {
                    first.SetResult();
                    return Task.CompletedTask;
                }),
                new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                {
                    second.SetResult();
                    return Task.CompletedTask;
                })
            ],
            TestContext.Current.CancellationToken
        );

        await Task.WhenAll(first.Task, second.Task);
        Assert.True(first.Task.IsCompletedSuccessfully);
        Assert.True(second.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Notifier_DispatchNotifications_AsyncFaultingHandler_ExceptionSwallowed()
    {
        // Covers the inline ContinueWith observe path in DispatchNotifications:
        // handler returns Task.FromException (already faulted, !IsCompletedSuccessfully),
        // triggering the ContinueWith observe that prevents an UnobservedTaskException.
        // The exception is intentionally swallowed — DispatchNotifications must not throw.
        var notifier = new Notifier(new ServiceCollection().BuildServiceProvider());

        var exception = await Record.ExceptionAsync(() =>
            notifier.DispatchNotifications(
                null,
                new NotificationMessage("value"),
                [
                    new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                        Task.FromException(new InvalidOperationException("handler fault")))
                ],
                TestContext.Current.CancellationToken
            ));

        // Exception must NOT propagate out of DispatchNotifications.
        Assert.Null(exception);
    }

    [Fact]
    public async Task Notifier_Notify_UsesPipelineForSingleAndBatchMessages()
    {
        var singleCount = 0;
        var batchCount = 0;
        var batchCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<INotificationHandler<NotificationMessage>>(
                new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                {
                    if (Interlocked.Increment(ref batchCount) >= 3)
                        batchCompletion.TrySetResult();
                    return Task.CompletedTask;
                })
            );
            services.AddSingleton<INotificationHandler<NotificationMessage>>(
                new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                {
                    Interlocked.Increment(ref singleCount);
                    return Task.CompletedTask;
                })
            );
            services.AddSingleton(sp =>
                new NotificationPipelineExecutor<NotificationMessage>(
                    sp,
                    NullLogger<NotificationPipelineExecutor<NotificationMessage>>.Instance
                )
            );
        });

        var notifier = new Notifier(provider);

        await notifier.Notify(null, new NotificationMessage("one"), TestContext.Current.CancellationToken);
        await notifier.Notify(
            "key",
            [new NotificationMessage("two"), new NotificationMessage("three")],
            TestContext.Current.CancellationToken
        );

        await batchCompletion.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(singleCount > 0);
        Assert.True(batchCount >= 3);
    }

    [Fact]
    public async Task Mediator_Send_WhenNoPipelineRegistered_CompletesWithoutError()
    {
        // Mediator.cs line 63: pipeline is null → early return
        using var provider = new ServiceCollection().BuildServiceProvider();
        var mediator = new Mediator(provider, new SpyNotifiable());

        // The pipeline is simply not registered → should complete without throwing
        var ex = await Record.ExceptionAsync(() =>
            mediator.Send(new CommandMessage(99), TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Mediator_Send_WithEmptyBatch_CompletesWithoutError()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<ICommandHandler<CommandMessage>, RecordingCommandHandler>();
            services.AddSingleton(sp =>
                new CommandPipelineExecutor<CommandMessage>(
                    sp,
                    NullLogger<CommandPipelineExecutor<CommandMessage>>.Instance
                )
            );
        });

        var mediator = new Mediator(provider, new SpyNotifiable());
        var exception = await Record.ExceptionAsync(() =>
            mediator.Send(Array.Empty<CommandMessage>(), TestContext.Current.CancellationToken)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task Mediator_Send_WhenNoHandlersRegistered_CompletesWithoutError()
    {
        using var provider = BuildProvider(services =>
            services.AddSingleton(sp =>
                new CommandPipelineExecutor<CommandMessage>(
                    sp,
                    NullLogger<CommandPipelineExecutor<CommandMessage>>.Instance
                )
            ));

        var mediator = new Mediator(provider, new SpyNotifiable());
        var exception = await Record.ExceptionAsync(() =>
            mediator.Send(new CommandMessage(12), TestContext.Current.CancellationToken)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task Mediator_Request_WhenPipelineThrowsMediatorException_PreservesExceptionTraceId()
    {
        // Mediator.cs lines 153-155: catch (MediatorException) { throw; }
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IRequestHandler<RequestMessage, Response>, RecordingRequestHandler>();
            services.AddSingleton<IPipelineRequestBehavior<RequestMessage, Response>, ThrowingMediatorExceptionRequestBehavior>();
            services.AddSingleton(sp =>
                new RequestPipelineExecutor<RequestMessage, Response>(
                    sp,
                    NullLogger<RequestPipelineExecutor<RequestMessage, Response>>.Instance
                )
            );
        });

        var mediator = new Mediator(provider, new SpyNotifiable());

        var exception = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.Request<RequestMessage, Response>(
                new RequestMessage(1),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal("trace-id-request", exception.TraceId);
    }

    [Fact]
    public async Task Notifier_DispatchNotifications_WithEmptyHandlers_ReturnsImmediately()
    {
        // Notifier.cs line 16: handlers.Length == 0 → return Task.CompletedTask
        var notifier = new Notifier(new ServiceCollection().BuildServiceProvider());

        // The handlers array is empty → should complete without throwing
        var ex = await Record.ExceptionAsync(() =>
            notifier.DispatchNotifications(
                null,
                new NotificationMessage("value"),
                [],
                TestContext.Current.CancellationToken
            ));
        Assert.Null(ex);
    }

    [Fact]
    public async Task Notifier_Notify_WhenNoPipelineRegistered_CompletesWithoutError()
    {
        // Notifier.cs line 33: pipeline is null → return Task.CompletedTask
        var notifier = new Notifier(new ServiceCollection().BuildServiceProvider());

        // The pipeline executor is not registered → should complete without throwing
        var ex = await Record.ExceptionAsync(() =>
            notifier.Notify(null, new NotificationMessage("value"), TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    private static ServiceProvider BuildProvider(Action<ServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static async Task<List<int>> ToList(IAsyncEnumerable<int> stream)
    {
        var items = new List<int>();
        await foreach (var item in stream.WithCancellation(TestContext.Current.CancellationToken))
            items.Add(item);
        return items;
    }

    private sealed class SpyNotifiable : INotifiable
    {
        public List<(object? Key, NotificationMessage Message)> SingleCalls { get; } = [];
        public List<(object? Key, NotificationMessage[] Messages)> BatchCalls { get; } = [];

        public Task DispatchNotifications<TMessage>(
            object? key,
            TMessage message,
            INotificationHandler<TMessage>[] handlers,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull => Task.CompletedTask;

        public Task Notify<TMessage>(object? key, TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull
        {
            SingleCalls.Add((key, Assert.IsType<NotificationMessage>(message)));
            return Task.CompletedTask;
        }

        public Task Notify<TMessage>(
            object? key,
            IEnumerable<TMessage> messages,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull
        {
            BatchCalls.Add((key, messages.Cast<NotificationMessage>().ToArray()));
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingCommandHandler : ICommandHandler<CommandMessage>
    {
        public List<int> Values { get; } = [];

        public Task Handle(CommandMessage message, CancellationToken cancellationToken = default)
        {
            Values.Add(message.Value);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingRequestHandler : IRequestHandler<RequestMessage, Response>
    {
        public Task<Response> Handle(
            RequestMessage message,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new Response(message.Value));
    }

    private sealed class RecordingStreamHandler(int multiplier) : IStreamHandler<StreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            StreamMessage message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            yield return message.Value * multiplier;
            yield return (message.Value + 1) * multiplier;
            await Task.CompletedTask;
        }
    }

    private sealed class ThrowingCommandBehavior : IPipelineCommandBehavior<CommandMessage>
    {
        public Task Handle(
            object? key,
            CommandMessage message,
            PipelineBehaviorDelegate<CommandMessage, Task> next,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("boom");
    }

    private sealed class ThrowingMediatorExceptionCommandBehavior : IPipelineCommandBehavior<CommandMessage>
    {
        public Task Handle(
            object? key,
            CommandMessage message,
            PipelineBehaviorDelegate<CommandMessage, Task> next,
            CancellationToken cancellationToken
        ) => throw new MediatorException(
            typeof(CommandMessage),
            typeof(ICommandHandler<CommandMessage>),
            "trace-id",
            new InvalidOperationException("boom")
        );
    }

    private sealed class ThrowingMediatorExceptionRequestBehavior : IPipelineRequestBehavior<RequestMessage, Response>
    {
        public Task<Response> Handle(
            object? key,
            RequestMessage message,
            PipelineBehaviorDelegate<RequestMessage, Task<Response>> next,
            CancellationToken cancellationToken
        ) => throw new MediatorException(
            typeof(RequestMessage),
            typeof(IRequestHandler<RequestMessage, Response>),
            "trace-id-request",
            new InvalidOperationException("boom")
        );
    }

    private sealed class ThrowingRequestBehavior : IPipelineRequestBehavior<RequestMessage, Response>
    {
        public Task<Response> Handle(
            object? key,
            RequestMessage message,
            PipelineBehaviorDelegate<RequestMessage, Task<Response>> next,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("boom");
    }

    private sealed class LambdaNotificationHandler<TMessage>(
        Func<TMessage, CancellationToken, Task> callback
    ) : INotificationHandler<TMessage>
        where TMessage : notnull
    {
        public Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }
}
