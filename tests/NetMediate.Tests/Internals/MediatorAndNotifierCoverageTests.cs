using System.Runtime.CompilerServices;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Tests.Internals;

public sealed class MediatorAndNotifierCoverageTests
{
    internal sealed record NotificationMessage(string Value);
    internal sealed record CommandMessage(int Value);
    internal sealed record RequestMessage(int Value);
    internal sealed record StreamMessage(int Value);
    internal sealed record Response(int Value);

    [Fact]
    public async Task Mediator_Notify_DelegatesSingleAndBatchMessages()
    {
        var notifier = new SpyNotifiable();
        var mediator = new Mediator{ ServiceProvider = new ServiceCollection().BuildServiceProvider(), Notifier = notifier };

        await mediator.Notify(new NotificationMessage("one"), TestContext.Current.CancellationToken);

        var callCount = notifier.CallCount;
        var firstCall = Assert.Single(notifier.Calls);

        Assert.Equal(1, callCount);
        Assert.Null(firstCall.Key);
        Assert.Equal("one", Assert.IsType<NotificationMessage>(firstCall.Message).Value);

        notifier.ClearCalls();

        await mediator.Notify(
            [new NotificationMessage("batch-one"), new NotificationMessage("batch-two")],
            TestContext.Current.CancellationToken
        );
        await mediator.Notify(
            "key",
            [new NotificationMessage("two"), new NotificationMessage("three")],
            TestContext.Current.CancellationToken
        );

        callCount = notifier.CallCount;
        var calls = notifier.Calls;

        Assert.Equal(4, callCount);
        Assert.Collection(
            calls,
            call =>
            {
                Assert.Null(call.Key);
                Assert.Equal("batch-one", Assert.IsType<NotificationMessage>(call.Message).Value);
            },
            call =>
            {
                Assert.Null(call.Key);
                Assert.Equal("batch-two", Assert.IsType<NotificationMessage>(call.Message).Value);
            },
            call =>
            {
                Assert.Equal("key", call.Key);
                Assert.Equal("two", Assert.IsType<NotificationMessage>(call.Message).Value);
            },
            call =>
            {
                Assert.Equal("key", call.Key);
                Assert.Equal("three", Assert.IsType<NotificationMessage>(call.Message).Value);
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
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };

        await mediator.Send(
            [new CommandMessage(1), new CommandMessage(2)],
            TestContext.Current.CancellationToken
        );

        Assert.Equal([1, 2], handler.Values);
    }

    [Fact]
    public async Task Mediator_Send_WithKey_UsesKeyedHandlers()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddKeyedSingleton<ICommandHandler<CommandMessage>, RecordingCommandHandler>("key-send");
        });

        var keyedHandler = Assert.IsType<RecordingCommandHandler>(
            provider.GetRequiredKeyedService<ICommandHandler<CommandMessage>>("key-send")
        );
        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };

        await mediator.Send("key-send", new CommandMessage(33), TestContext.Current.CancellationToken);

        Assert.Equal([33], keyedHandler.Values);
    }

    [Fact]
    public async Task Mediator_Send_WhenPipelineThrows_WrapsException()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<ICommandHandler<CommandMessage>, RecordingCommandHandler>();
            services.AddSingleton<ICommandHandler<CommandMessage>, ThrowingCommandHandler>();
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };
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
            services.AddSingleton<ICommandHandler<CommandMessage>, ThrowingCommandHandler>();
        });

        using var activity = new Activity("send").Start();
        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };

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
            services.AddSingleton<ICommandHandler<CommandMessage>, ThrowingMediatorExceptionCommandHandler>();
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };

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
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };
        var response = await mediator.Request<RequestMessage, Response>(
            new RequestMessage(7),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(7, response.Value);
    }

    [Fact]
    public async Task Mediator_Request_WithKey_UsesKeyedHandler()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddKeyedSingleton<IRequestHandler<RequestMessage, Response>, RecordingRequestHandler>(
                "key-request"
            );
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };
        var response = await mediator.Request<RequestMessage, Response>(
            "key-request",
            new RequestMessage(13),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(13, response.Value);
    }

    [Fact]
    public async Task Mediator_Request_WhenPipelineConstructionFails_WrapsException()
    {
        using var provider = BuildProvider(services =>
        {
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };

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
            services.AddSingleton<IRequestHandler<RequestMessage, Response>, ThrowingRequestHandler>();
        });

        using var activity = new Activity("request").Start();
        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };

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
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };
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
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };
        var items = await ToList(
            mediator.RequestStream<StreamMessage, int>(
                new StreamMessage(3),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal([6, 8], items);
    }

    [Fact]
    public async Task Mediator_RequestStream_WithKey_UsesKeyedStreamHandlers()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddKeyedSingleton<IStreamHandler<StreamMessage, int>, KeyedStreamHandler>("key-stream");
        });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };
        var items = await ToList(
            mediator.RequestStream<StreamMessage, int>(
                "key-stream",
                new StreamMessage(3),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal([300, 400], items);
    }

    [Fact]
    public async Task Mediator_RequestStream_WithoutHandlers_ReturnsEmptyStream()
    {
        using var provider = BuildProvider(_ => { });

        var mediator = new Mediator{ ServiceProvider = provider, Notifier = new SpyNotifiable() };
        var items = await ToList(
            mediator.RequestStream<StreamMessage, int>(
                new StreamMessage(3),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Empty(items);
    }

    [Fact]
    public async Task Notifier_DispatchNotifications_InvokesAllHandlers()
    {
        var notifier = new Notifier{ ServiceProvider = new ServiceCollection().BuildServiceProvider() };
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
        var notifier = new Notifier{ ServiceProvider = new ServiceCollection().BuildServiceProvider() };

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
    public async Task Notifier_DispatchNotifications_IncompleteFaultingHandler_ExceptionSwallowed()
    {
        var notifier = new Notifier{ ServiceProvider = new ServiceCollection().BuildServiceProvider() };
        var handlerTask = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exception = await Record.ExceptionAsync(() =>
            notifier.DispatchNotifications(
                null,
                new NotificationMessage("value"),
                [
                    new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                        handlerTask.Task)
                ],
                TestContext.Current.CancellationToken
            ));

        handlerTask.TrySetException(new InvalidOperationException("late handler fault"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => handlerTask.Task);

        Assert.Null(exception);
    }

    [Fact]
    public async Task Notifier_DispatchNotifications_SyncFaultingHandler_ContinuesDispatch()
    {
        var notifier = new Notifier{ ServiceProvider = new ServiceCollection().BuildServiceProvider() };
        var invoked = false;

        var exception = await Record.ExceptionAsync(() =>
            notifier.DispatchNotifications(
                null,
                new NotificationMessage("value"),
                [
                    new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                        throw new InvalidOperationException("sync handler fault")),
                    new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                    {
                        invoked = true;
                        return Task.CompletedTask;
                    })
                ],
                TestContext.Current.CancellationToken
            ));

        Assert.Null(exception);
        Assert.True(invoked);
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
        });

        var notifier = new Notifier{ ServiceProvider = provider };
        var mediator = new Mediator { ServiceProvider = provider, Notifier = notifier };

        await mediator.Notify(null, new NotificationMessage("one"), TestContext.Current.CancellationToken);
        await mediator.Notify(
            null,
            [new NotificationMessage("two"), new NotificationMessage("three")],
            TestContext.Current.CancellationToken
        );

        await batchCompletion.Task.WaitAsync(TestContext.Current.CancellationToken);
        Assert.True(singleCount > 0);
        Assert.True(batchCount >= 3);
    }

    [Fact]
    public async Task Notifier_Notify_WithKey_UsesKeyedHandlers()
    {
        var keyedCount = 0;
        var unkeyedCount = 0;
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<INotificationHandler<NotificationMessage>>(
                new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                {
                    Interlocked.Increment(ref unkeyedCount);
                    return Task.CompletedTask;
                })
            );
            services.AddKeyedSingleton<INotificationHandler<NotificationMessage>>(
                "key-notify",
                new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                {
                    Interlocked.Increment(ref keyedCount);
                    return Task.CompletedTask;
                })
            );
        });

        var notifier = new Notifier{ ServiceProvider = provider };
        var mediator = new Mediator { ServiceProvider = provider, Notifier = notifier };
        await mediator.Notify(
            "key-notify",
            new NotificationMessage("k"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(1, keyedCount);
        Assert.Equal(0, unkeyedCount);
    }

    [Fact]
    public async Task Mediator_Send_WhenNoPipelineRegistered_CompletesWithoutError()
    {
        // Mediator.cs line 63: pipeline is null → early return
        using var provider = new ServiceCollection().BuildServiceProvider();
        var mediator = new Mediator { ServiceProvider = provider, Notifier = new SpyNotifiable() };

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
        });

        var mediator = new Mediator { ServiceProvider = provider, Notifier = new SpyNotifiable() };
        var exception = await Record.ExceptionAsync(() =>
            mediator.Send(Array.Empty<CommandMessage>(), TestContext.Current.CancellationToken)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task Mediator_Send_WhenNoHandlersRegistered_CompletesWithoutError()
    {
        using var provider = BuildProvider(_ => { });

        var mediator = new Mediator { ServiceProvider = provider, Notifier = new SpyNotifiable() };
        var exception = await Record.ExceptionAsync(() =>
            mediator.Send(new CommandMessage(12), TestContext.Current.CancellationToken)
        );

        Assert.Null(exception);
    }

    [Fact]
    public async Task Mediator_Request_WhenPipelineThrowsMediatorException_PreservesExceptionTraceId()
    {
        using var provider = BuildProvider(services =>
        {
            services.AddSingleton<IRequestHandler<RequestMessage, Response>, RecordingRequestHandler>();
            services.AddSingleton<IRequestHandler<RequestMessage, Response>, ThrowingMediatorExceptionRequestHandler>();
        });

        var mediator = new Mediator { ServiceProvider = provider, Notifier = new SpyNotifiable() };

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
        var notifier = new Notifier{ ServiceProvider = new ServiceCollection().BuildServiceProvider() };

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
        var notifier = new Notifier{ ServiceProvider = new ServiceCollection().BuildServiceProvider() };
        var mediator = new Mediator { ServiceProvider = notifier.ServiceProvider, Notifier = notifier };

        // The pipeline executor is not registered → should complete without throwing
        var ex = await Record.ExceptionAsync(() =>
            mediator.Notify(null, new NotificationMessage("value"), TestContext.Current.CancellationToken));
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

    internal sealed class SpyNotifiable : INotifiable
    {
        private static readonly Lock s_lock = new();

        private int _callCount = 0;
        private readonly List<(object? Key, object Message)> _calls = [];

        public int CallCount
        {
            get
            {
                lock (s_lock)
                {
                    return _callCount;
                }
            }
        }

        public IReadOnlyList<(object? Key, object Message)> Calls
        {
            get
            {
                lock (s_lock)
                {
                    return [.. _calls];
                }
            }
        }

        public Task DispatchNotifications<TMessage>(
            object? key,
            TMessage message,
            INotificationHandler<TMessage>[] handlers,
            CancellationToken cancellationToken = default
        )
            where TMessage : notnull
        {
            lock (s_lock)
            {
                Interlocked.Increment(ref _callCount);
                _calls.Add((key, message));
            }

            return Task.CompletedTask;
        }

        public void ClearCalls()
        {
            lock (s_lock)
            {
                Interlocked.Exchange(ref _callCount, 0);
                _calls.Clear();
            }
        }
    }

    internal sealed class RecordingCommandHandler : ICommandHandler<CommandMessage>
    {
        public List<int> Values { get; } = [];

        public Task Handle(CommandMessage message, CancellationToken cancellationToken = default)
        {
            Values.Add(message.Value);
            return Task.CompletedTask;
        }
    }

    internal sealed class RecordingRequestHandler : IRequestHandler<RequestMessage, Response>
    {
        public Task<Response> Handle(
            RequestMessage message,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new Response(message.Value));
    }

    internal sealed class RecordingStreamHandler(int multiplier) : IStreamHandler<StreamMessage, int>
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

    internal sealed class KeyedStreamHandler : IStreamHandler<StreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            StreamMessage message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            yield return message.Value * 100;
            yield return (message.Value + 1) * 100;
            await Task.CompletedTask;
        }
    }

    internal sealed class ThrowingCommandHandler : ICommandHandler<CommandMessage>
    {
        public Task Handle(
            CommandMessage message,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("boom");
    }

    internal sealed class ThrowingMediatorExceptionCommandHandler : ICommandHandler<CommandMessage>
    {
        public Task Handle(
            CommandMessage message,
            CancellationToken cancellationToken = default
        ) => throw new MediatorException(
            typeof(CommandMessage),
            typeof(ICommandHandler<CommandMessage>),
            "trace-id",
            new InvalidOperationException("boom")
        );
    }

    internal sealed class ThrowingMediatorExceptionRequestHandler : IRequestHandler<RequestMessage, Response>
    {
        public Task<Response> Handle(RequestMessage message, CancellationToken cancellationToken = default) =>
            throw new MediatorException(
            typeof(RequestMessage),
            typeof(IRequestHandler<RequestMessage, Response>),
            "trace-id-request",
            new InvalidOperationException("boom")
        );
    }

    internal sealed class ThrowingRequestHandler : IRequestHandler<RequestMessage, Response>
    {
        public Task<Response> Handle(RequestMessage message, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("boom");
    }

    internal sealed class LambdaNotificationHandler<TMessage>(
        Func<TMessage, CancellationToken, Task> callback
    ) : INotificationHandler<TMessage>
        where TMessage : notnull
    {
        public Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }
}
