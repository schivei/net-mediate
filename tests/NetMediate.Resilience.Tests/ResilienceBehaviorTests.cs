using Microsoft.Extensions.Options;

[assembly: GenDI.GenDICoveration(false)]

namespace NetMediate.Resilience.Tests;

public sealed class ResilienceBehaviorTests
{
    private sealed record RetryRequestMessage;
    private sealed record RetryDisabledMessage;
    private sealed record RetryCommandMessage;
    private sealed record RetryNotificationMessage;
    private sealed record TimeoutRequestMessage;
    private sealed record TimeoutCommandMessage;
    private sealed record TimeoutNotificationMessage;
    private sealed record CircuitRequestMessage;
    private sealed record CircuitCommandMessage;
    private sealed record CircuitNotificationMessage;
    private sealed record Response(int Value);

    [Fact]
    public async Task RetryRequestBehavior_RetriesUntilSuccess()
    {
        var attempts = 0;
        var behavior = new TestRetryRequestBehavior<RetryRequestMessage, Response>(
            new LambdaRequestHandler<RetryRequestMessage, Response>((_, _) =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<Response>(new InvalidOperationException("fail"))
                    : Task.FromResult(new Response(42));
            }),
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 2 })
        );

        var response = await behavior.Handle(
            new RetryRequestMessage(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, attempts);
        Assert.Equal(42, response.Value);
    }

    [Fact]
    public async Task RetryRequestBehavior_WhenDisabled_DoesNotRetry()
    {
        var attempts = 0;
        var behavior = new TestRetryRequestBehavior<RetryDisabledMessage, Response>(
            new LambdaRequestHandler<RetryDisabledMessage, Response>((_, _) =>
            {
                attempts++;
                return Task.FromException<Response>(new InvalidOperationException("fail"));
            }),
            Options.Create(
                new RetryBehaviorOptions
                {
                    Disabled = true,
                    MaxRetryCount = 5,
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new RetryDisabledMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryCommandBehavior_RetriesOperationCanceledException()
    {
        var attempts = 0;
        var behavior = new TestRetryCommandBehavior<RetryCommandMessage>(
            new LambdaCommandHandler<RetryCommandMessage>((_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromCanceled(new CancellationToken(canceled: true))
                    : Task.CompletedTask;
            }),
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 1 })
        );

        await behavior.Handle(new RetryCommandMessage(), TestContext.Current.CancellationToken);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryNotificationBehavior_RetriesUntilSuccess()
    {
        var attempts = 0;
        var behavior = new TestRetryNotificationBehavior<RetryNotificationMessage>(
            new LambdaNotificationHandler<RetryNotificationMessage>((_, _) =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException(new InvalidOperationException("fail"))
                    : Task.CompletedTask;
            }),
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 2 })
        );

        await behavior.Handle(new RetryNotificationMessage(), TestContext.Current.CancellationToken);
        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task TimeoutRequestBehavior_ThrowsTimeoutException_WhenElapsed()
    {
        var behavior = new TestTimeoutRequestBehavior<TimeoutRequestMessage, Response>(
            new LambdaRequestHandler<TimeoutRequestMessage, Response>(async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                return new Response(1);
            }),
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(10),
                }
            )
        );

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            behavior.Handle(new TimeoutRequestMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Request exceeded timeout", exception.Message);
    }

    [Fact]
    public async Task TimeoutCommandBehavior_WhenDisabled_BypassesTimeout()
    {
        var called = false;
        var behavior = new TestTimeoutCommandBehavior<TimeoutCommandMessage>(
            new LambdaCommandHandler<TimeoutCommandMessage>((_, _) =>
            {
                called = true;
                return Task.CompletedTask;
            }),
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    Disabled = true,
                    CommandTimeout = TimeSpan.FromMilliseconds(1),
                }
            )
        );

        await behavior.Handle(new TimeoutCommandMessage(), TestContext.Current.CancellationToken);
        Assert.True(called);
    }

    [Fact]
    public async Task TimeoutNotificationBehavior_ThrowsTimeoutException_WhenElapsed()
    {
        var behavior = new TestTimeoutNotificationBehavior<TimeoutNotificationMessage>(
            new LambdaNotificationHandler<TimeoutNotificationMessage>(async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
            }),
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    NotificationTimeout = TimeSpan.FromMilliseconds(10),
                }
            )
        );

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            behavior.Handle(new TimeoutNotificationMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Notification exceeded timeout", exception.Message);
    }

    [Fact]
    public async Task CircuitBreakerRequestBehavior_OpensAndResetsCircuit()
    {
        var attempts = 0;
        var behavior = new CircuitBreakerRequestBehavior<CircuitRequestMessage, Response>(
            new LambdaRequestHandler<CircuitRequestMessage, Response>((_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException<Response>(new InvalidOperationException("boom"))
                    : Task.FromResult(new Response(7));
            }),
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    FailureThreshold = 1,
                    OpenDuration = TimeSpan.FromMilliseconds(100),
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitRequestMessage(), TestContext.Current.CancellationToken)
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitRequestMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Circuit open for request", openException.Message);

        var response = await WaitUntilSucceedsAsync(() =>
            behavior.Handle(new CircuitRequestMessage(), TestContext.Current.CancellationToken)
        );
        Assert.Equal(7, response.Value);
    }

    [Fact]
    public async Task CircuitBreakerRequestBehavior_WhenDisabled_BypassesCircuit()
    {
        var behavior = new CircuitBreakerRequestBehavior<CircuitRequestMessage, Response>(
            new LambdaRequestHandler<CircuitRequestMessage, Response>((_, _) => Task.FromResult(new Response(5))),
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    Disabled = true,
                    FailureThreshold = 1,
                }
            )
        );

        var response = await behavior.Handle(
            new CircuitRequestMessage(),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(5, response.Value);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_CompletesOnSuccess()
    {
        var called = false;
        var behavior = new CircuitBreakerCommandBehavior<CircuitCommandMessage>(
            new LambdaCommandHandler<CircuitCommandMessage>((_, _) =>
            {
                called = true;
                return Task.CompletedTask;
            }),
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        await behavior.Handle(new CircuitCommandMessage(), TestContext.Current.CancellationToken);
        Assert.True(called);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_WhenThresholdReached_OpensCircuit()
    {
        var attempts = 0;
        var behavior = new CircuitBreakerCommandBehavior<CircuitCommandMessage>(
            new LambdaCommandHandler<CircuitCommandMessage>((_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new InvalidOperationException("boom"))
                    : Task.CompletedTask;
            }),
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    FailureThreshold = 1,
                    OpenDuration = TimeSpan.FromMilliseconds(20),
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitCommandMessage(), TestContext.Current.CancellationToken)
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitCommandMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Circuit open for command", openException.Message);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_WhenDisabled_BypassesCircuit()
    {
        var called = false;
        var behavior = new CircuitBreakerCommandBehavior<CircuitCommandMessage>(
            new LambdaCommandHandler<CircuitCommandMessage>((_, _) =>
            {
                called = true;
                return Task.CompletedTask;
            }),
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    Disabled = true,
                    FailureThreshold = 1,
                }
            )
        );

        await behavior.Handle(new CircuitCommandMessage(), TestContext.Current.CancellationToken);
        Assert.True(called);
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_RethrowsFailures()
    {
        var behavior = new CircuitBreakerNotificationBehavior<CircuitNotificationMessage>(
            new LambdaNotificationHandler<CircuitNotificationMessage>((_, _) =>
                Task.FromException(new InvalidOperationException("boom"))),
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitNotificationMessage(), TestContext.Current.CancellationToken)
        );
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_CompletesOnSuccess()
    {
        var called = false;
        var behavior = new CircuitBreakerNotificationBehavior<CircuitNotificationMessage>(
            new LambdaNotificationHandler<CircuitNotificationMessage>((_, _) =>
            {
                called = true;
                return Task.CompletedTask;
            }),
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        await behavior.Handle(new CircuitNotificationMessage(), TestContext.Current.CancellationToken);
        Assert.True(called);
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_WhenThresholdReached_OpensCircuit()
    {
        var attempts = 0;
        var behavior = new CircuitBreakerNotificationBehavior<CircuitNotificationMessage>(
            new LambdaNotificationHandler<CircuitNotificationMessage>((_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException(new InvalidOperationException("boom"))
                    : Task.CompletedTask;
            }),
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    FailureThreshold = 1,
                    OpenDuration = TimeSpan.FromMilliseconds(20),
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitNotificationMessage(), TestContext.Current.CancellationToken)
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitNotificationMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Circuit open for notification", openException.Message);
    }

    [Fact]
    public async Task RetryRequestBehavior_WhenDelayIsConfigured_WaitsBeforeRetrying()
    {
        var attempts = 0;
        var startedAt = DateTimeOffset.UtcNow;
        var behavior = new TestRetryRequestBehavior<RetryRequestMessage, Response>(
            new LambdaRequestHandler<RetryRequestMessage, Response>((_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException<Response>(new InvalidOperationException("fail"))
                    : Task.FromResult(new Response(9));
            }),
            Options.Create(
                new RetryBehaviorOptions
                {
                    MaxRetryCount = 1,
                    Delay = TimeSpan.FromMilliseconds(25),
                }
            )
        );

        var response = await behavior.Handle(
            new RetryRequestMessage(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, attempts);
        Assert.Equal(9, response.Value);
        Assert.True(DateTimeOffset.UtcNow - startedAt >= TimeSpan.FromMilliseconds(20));
    }

    private static async Task<T> WaitUntilSucceedsAsync<T>(Func<Task<T>> action)
    {
        Exception? lastException = null;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                return await action();
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Circuit open"))
            {
                lastException = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(20), TestContext.Current.CancellationToken);
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Expected circuit to close within the retry window, but it stayed open. Last error: {lastException?.Message}"
        );
    }

    private sealed class LambdaRequestHandler<TMessage, TResponse>(
        Func<TMessage, CancellationToken, Task<TResponse>> callback
    ) : IRequestHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        public Task<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class LambdaCommandHandler<TMessage>(
        Func<TMessage, CancellationToken, Task> callback
    ) : ICommandHandler<TMessage>
        where TMessage : notnull
    {
        public Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class LambdaNotificationHandler<TMessage>(
        Func<TMessage, CancellationToken, Task> callback
    ) : INotificationHandler<TMessage>
        where TMessage : notnull
    {
        public Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class TestRetryRequestBehavior<TMessage, TResponse>(
        IRequestHandler<TMessage, TResponse> handler,
        IOptions<RetryBehaviorOptions> optionsAccessor
    ) : RetryRequestBehavior<TMessage, TResponse>(handler, optionsAccessor)
        where TMessage : notnull;

    private sealed class TestRetryCommandBehavior<TMessage>(
        ICommandHandler<TMessage> handler,
        IOptions<RetryBehaviorOptions> optionsAccessor
    ) : RetryCommandBehavior<TMessage>(handler, optionsAccessor)
        where TMessage : notnull;

    private sealed class TestRetryNotificationBehavior<TMessage>(
        INotificationHandler<TMessage> handler,
        IOptions<RetryBehaviorOptions> optionsAccessor
    ) : RetryNotificationBehavior<TMessage>(handler, optionsAccessor)
        where TMessage : notnull;

    private sealed class TestTimeoutRequestBehavior<TMessage, TResponse>(
        IRequestHandler<TMessage, TResponse> handler,
        IOptions<TimeoutBehaviorOptions> optionsAccessor
    ) : TimeoutRequestBehavior<TMessage, TResponse>(handler, optionsAccessor)
        where TMessage : notnull;

    private sealed class TestTimeoutCommandBehavior<TMessage>(
        ICommandHandler<TMessage> handler,
        IOptions<TimeoutBehaviorOptions> optionsAccessor
    ) : TimeoutCommandBehavior<TMessage>(handler, optionsAccessor)
        where TMessage : notnull;

    private sealed class TestTimeoutNotificationBehavior<TMessage>(
        INotificationHandler<TMessage> handler,
        IOptions<TimeoutBehaviorOptions> optionsAccessor
    ) : TimeoutNotificationBehavior<TMessage>(handler, optionsAccessor)
        where TMessage : notnull;
}
