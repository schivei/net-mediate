using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.Internals;
using MoqNotifier = NetMediate.Moq.Notifier;

namespace NetMediate.Tests.Internals;

public class MediatorTests
{
    public MediatorTests()
    {
        // Clear the static handler cache before each test to prevent cross-test contamination.
        // Since each test builds a fresh ServiceProvider with different handler registrations,
        // the static cache must be reset so earlier registrations don't leak into later tests.
        Extensions.ClearCache();
    }

    private static ServiceProvider BuildProvider(Action<IMediatorServiceBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetMediate(configure);
        return services.BuildServiceProvider();
    }

    private static Mediator BuildMediator(IServiceProvider provider)
    {
        var notifier = new MoqNotifier(provider);
        return new Mediator(provider, notifier);
    }

    private static async Task WaitForAsync(Func<bool> predicate, CancellationToken ct)
    {
        for (var i = 0; i < 100; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (predicate()) return;
            await Task.Delay(10, ct);
        }
    }

    #region Notify Tests

    [Fact]
    public async Task Notify_WithValidMessage_ShouldInvokeHandler()
    {
        var handler = new TrackingNotificationHandler<TestMessageNotification>();
        await using var provider = BuildProvider(b =>
            b.RegisterNotificationHandler<TestMessageNotification>(handler));
        var mediator = BuildMediator(provider);
        var message = new TestMessageNotification { Content = "Test" };

        await mediator.Notify(message, TestContext.Current.CancellationToken);
        await WaitForAsync(() => handler.Invocations.Contains(message), TestContext.Current.CancellationToken);

        Assert.Contains(message, handler.Invocations);
    }

    [Fact]
    public async Task Notify_Enumerable_ShouldInvokeAllHandlers()
    {
        var handler = new TrackingNotificationHandler<TestMessageNotification>();
        await using var provider = BuildProvider(b =>
            b.RegisterNotificationHandler<TestMessageNotification>(handler));
        var mediator = BuildMediator(provider);
        TestMessageNotification[] messages = [new() { Content = "1" }, new() { Content = "2" }];

        await mediator.Notify((IEnumerable<TestMessageNotification>)messages, TestContext.Current.CancellationToken);
        await WaitForAsync(() => handler.Invocations.Count >= 2, TestContext.Current.CancellationToken);

        Assert.Contains(messages[0], handler.Invocations);
        Assert.Contains(messages[1], handler.Invocations);
    }

    [Fact]
    public async Task Notify_WithNoHandlers_ShouldNotThrow()
    {
        await using var provider = BuildProvider(_ => { });
        var mediator = BuildMediator(provider);

        var task = mediator.Notify(new TestMessageNotification { Content = "Test" }, TestContext.Current.CancellationToken);
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Notifies_WithValidMessageAndHandlers_ShouldCallAllHandlers()
    {
        var handler1 = new TrackingNotificationHandler<TestMessageNotification>();
        var handler2 = new TrackingNotificationHandler<TestMessageNotification>();
        await using var provider = BuildProvider(b =>
        {
            b.RegisterNotificationHandler<TestMessageNotification>(handler1);
            b.RegisterNotificationHandler<TestMessageNotification>(handler2);
        });
        var mediator = BuildMediator(provider);
        var message = new TestMessageNotification { Content = "Test" };

        await mediator.Notify(message, TestContext.Current.CancellationToken);
        await WaitForAsync(
            () => handler1.Invocations.Contains(message) && handler2.Invocations.Contains(message),
            TestContext.Current.CancellationToken
        );

        Assert.Contains(message, handler1.Invocations);
        Assert.Contains(message, handler2.Invocations);
    }

    #endregion

    #region Send Tests

    [Fact]
    public async Task Send_WithValidMessageAndHandler_ShouldCallHandler()
    {
        var handler = new TrackingCommandHandler<TestMessageCommand>();
        await using var provider = BuildProvider(b =>
            b.RegisterCommandHandler<TestMessageCommand>(handler));
        var mediator = BuildMediator(provider);
        var message = new TestMessageCommand { Content = "Test" };

        await mediator.Send(message, TestContext.Current.CancellationToken);

        Assert.Single(handler.Invocations, message);
    }

    [Fact]
    public async Task Send_WithNoHandler_ShouldCompleteWithoutException()
    {
        await using var provider = BuildProvider(_ => { });
        var mediator = BuildMediator(provider);

        // No throw — Send is a no-op when no executor is registered for the message type
        var task = mediator.Send(new TestMessageCommand { Content = "Test" }, TestContext.Current.CancellationToken);
        await task;

        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Send_WhenHandlerThrows_PropagatesException()
    {
        await using var provider = BuildProvider(b =>
            b.RegisterCommandHandler<TestMessageCommand>(new ThrowingCommandHandler()));
        var mediator = BuildMediator(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.Send(new TestMessageCommand { Content = "Test" }, TestContext.Current.CancellationToken)
        );
    }

    #endregion

    #region Request Tests

    [Fact]
    public async Task Request_WithValidMessageAndHandler_ShouldReturnResponse()
    {
        var expectedResponse = new TestResponse { Value = "Response" };
        await using var provider = BuildProvider(b =>
            b.RegisterRequestHandler<TestRequest, TestResponse>(new FixedResponseHandler(expectedResponse)));
        var mediator = BuildMediator(provider);

        var result = await mediator.Request<TestRequest, TestResponse>(
            new TestRequest { Id = 1 },
            TestContext.Current.CancellationToken
        );

        Assert.Same(expectedResponse, result);
    }

    [Fact]
    public async Task Request_WithNoHandler_ShouldThrowInvalidOperationException()
    {
        await using var provider = BuildProvider(_ => { });
        var mediator = BuildMediator(provider);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await mediator.Request<TestRequest, TestResponse>(
                new TestRequest { Id = 1 },
                TestContext.Current.CancellationToken
            )
        );
    }

    #endregion

    #region RequestStream Tests

    [Fact]
    public async Task RequestStream_WithValidMessageAndHandler_ShouldReturnStream()
    {
        var responses = new[]
        {
            new TestResponse { Value = "Response1" },
            new TestResponse { Value = "Response2" },
        };
        await using var provider = BuildProvider(b =>
            b.RegisterStreamHandler<TestStream, TestResponse>(new EnumerableStreamHandler(responses)));
        var mediator = BuildMediator(provider);

        var results = new List<TestResponse>();
        await foreach (var item in mediator.RequestStream<TestStream, TestResponse>(
            new TestStream { Id = 1 },
            TestContext.Current.CancellationToken))
        {
            results.Add(item);
        }

        Assert.Equal(2, results.Count);
        Assert.Equal("Response1", results[0].Value);
        Assert.Equal("Response2", results[1].Value);
    }

    [Fact]
    public void RequestStream_WithNoHandler_ShouldThrowInvalidOperationException()
    {
        using var provider = BuildProvider(_ => { });
        var mediator = BuildMediator(provider);

        Assert.Throws<InvalidOperationException>(() =>
            mediator.RequestStream<TestStream, TestResponse>(
                new TestStream { Id = 1 },
                TestContext.Current.CancellationToken
            )
        );
    }

    #endregion

    #region Send Enumerable Tests

    [Fact]
    public async Task Send_Enumerable_ShouldInvokeAllHandlers()
    {
        var handler = new TrackingCommandHandler<TestMessageCommand>();
        await using var provider = BuildProvider(b => b.RegisterCommandHandler<TestMessageCommand>(handler));
        var mediator = BuildMediator(provider);
        TestMessageCommand[] commands = [new() { Content = "A" }, new() { Content = "B" }];

        await mediator.Send((IEnumerable<TestMessageCommand>)commands, TestContext.Current.CancellationToken);

        Assert.Contains(commands[0], handler.Invocations);
        Assert.Contains(commands[1], handler.Invocations);
    }

    #endregion

    #region Notify Error-Path Tests

    [Fact]
    public async Task Notify_WhenNotifierThrows_PropagatesException()
    {
        await using var provider = BuildProvider(_ => { });
        var mediator = new Mediator(provider, new ThrowingNotifier());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.Notify(new TestMessageNotification { Content = "Test" }, TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task Notify_Enumerable_WhenNotifierThrows_PropagatesException()
    {
        await using var provider = BuildProvider(_ => { });
        var mediator = new Mediator(provider, new ThrowingNotifier());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.Notify(
                (IEnumerable<TestMessageNotification>)[new() { Content = "Test" }],
                TestContext.Current.CancellationToken
            )
        );
    }

    #endregion

    #region Helpers

    private sealed class TrackingNotificationHandler<T> : INotificationHandler<T> where T : notnull
    {
        public ConcurrentBag<T> Invocations { get; } = [];

        public Task Handle(T notification, CancellationToken ct = default)
        {
            Invocations.Add(notification);
            return Task.CompletedTask;
        }
    }

    private sealed class TrackingCommandHandler<T> : ICommandHandler<T> where T : notnull
    {
        public ConcurrentBag<T> Invocations { get; } = [];

        public Task Handle(T command, CancellationToken ct = default)
        {
            Invocations.Add(command);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingCommandHandler : ICommandHandler<TestMessageCommand>
    {
        public Task Handle(TestMessageCommand command, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("handler error"));
    }

    private sealed class FixedResponseHandler(TestResponse response) : IRequestHandler<TestRequest, TestResponse>
    {
        public Task<TestResponse> Handle(TestRequest request, CancellationToken ct = default) =>
            Task.FromResult(response);
    }

    private sealed class EnumerableStreamHandler(IEnumerable<TestResponse> items)
        : IStreamHandler<TestStream, TestResponse>
    {
        public async IAsyncEnumerable<TestResponse> Handle(
            TestStream request,
            [EnumeratorCancellation] CancellationToken ct = default
        )
        {
            foreach (var item in items)
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                await Task.Delay(1, ct);
            }
        }
    }

    private sealed class ThrowingNotifier : INotifiable
    {
        public Task DispatchNotifications<TMessage>(TMessage message, INotificationHandler<TMessage>[] handlers,
            CancellationToken cancellationToken = default) where TMessage : notnull => Task.CompletedTask;

        // Throws synchronously so the catch block in Mediator.Notify is exercised.
        public Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default)
            where TMessage : notnull => throw new InvalidOperationException("notifier error");

        public Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
            where TMessage : notnull => throw new InvalidOperationException("notifier error");
    }

    #endregion

    #region Test Types

    public class TestMessage
    {
        public string Content { get; set; } = string.Empty;
    }

    public class TestMessageNotification : TestMessage;

    public class TestMessageCommand : TestMessage;

    public class TestRequest
    {
        public int Id { get; set; }
    }

    public class TestStream
    {
        public int Id { get; set; }
    }

    public class TestResponse
    {
        public string Value { get; set; } = string.Empty;
    }

    #endregion
}

