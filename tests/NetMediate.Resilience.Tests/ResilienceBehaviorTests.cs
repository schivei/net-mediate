using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

[assembly: ExcludeFromCodeCoverage]
[assembly: GenDICoveration(false)]

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
    private sealed record CircuitStreamMessage;
    private sealed record RetryStreamMessage;
    private sealed record RetryStreamDisabledMessage;
    private sealed record TimeoutStreamMessage;
    private sealed record TimeoutStreamDisabledMessage;
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
                    ? ValueTask.FromException<Response>(new InvalidOperationException("fail"))
                    : ValueTask.FromResult(new Response(42));
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
                return ValueTask.FromException<Response>(new InvalidOperationException("fail"));
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
            behavior.Handle(new RetryDisabledMessage(), TestContext.Current.CancellationToken).AsTask()
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
                    ? ValueTask.FromCanceled(new CancellationToken(canceled: true))
                    : ValueTask.CompletedTask;
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
                    ? ValueTask.FromException(new InvalidOperationException("fail"))
                    : ValueTask.CompletedTask;
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
            behavior.Handle(new TimeoutRequestMessage(), TestContext.Current.CancellationToken).AsTask()
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
                return ValueTask.CompletedTask;
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
            behavior.Handle(new TimeoutNotificationMessage(), TestContext.Current.CancellationToken).AsTask()
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
                    ? ValueTask.FromException<Response>(new InvalidOperationException("boom"))
                    : ValueTask.FromResult(new Response(7));
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
            behavior.Handle(new CircuitRequestMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitRequestMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Contains("Circuit open for request", openException.Message);

        var response = await WaitUntilSucceedsAsync(() =>
            behavior.Handle(new CircuitRequestMessage(), TestContext.Current.CancellationToken).AsTask()
        );
        Assert.Equal(7, response.Value);
    }

    [Fact]
    public async Task CircuitBreakerRequestBehavior_WhenDisabled_BypassesCircuit()
    {
        var behavior = new CircuitBreakerRequestBehavior<CircuitRequestMessage, Response>(
            new LambdaRequestHandler<CircuitRequestMessage, Response>((_, _) => ValueTask.FromResult(new Response(5))),
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
    public async Task CircuitBreakerCommandBehavior_WhenThresholdReached_OpensCircuit()
    {
        var attempts = 0;
        var behavior = new CircuitBreakerCommandBehavior<CircuitCommandMessage>(
            new LambdaCommandHandler<CircuitCommandMessage>((_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? ValueTask.FromException(new InvalidOperationException("boom"))
                    : ValueTask.CompletedTask;
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
            behavior.Handle(new CircuitCommandMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitCommandMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Contains("Circuit open for command", openException.Message);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_WhenOpenDurationIsNonPositive_UsesFallbackDuration()
    {
        var attempts = 0;
        var behavior = new CircuitBreakerCommandBehavior<CircuitCommandMessage>(
            new LambdaCommandHandler<CircuitCommandMessage>((_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? ValueTask.FromException(new InvalidOperationException("boom"))
                    : ValueTask.CompletedTask;
            }),
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    FailureThreshold = 1,
                    OpenDuration = TimeSpan.Zero,
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitCommandMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitCommandMessage(), TestContext.Current.CancellationToken).AsTask()
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
                return ValueTask.CompletedTask;
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
                ValueTask.FromException(new InvalidOperationException("boom"))),
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitNotificationMessage(), TestContext.Current.CancellationToken).AsTask()
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
                return ValueTask.CompletedTask;
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
                    ? ValueTask.FromException(new InvalidOperationException("boom"))
                    : ValueTask.CompletedTask;
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
            behavior.Handle(new CircuitNotificationMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitNotificationMessage(), TestContext.Current.CancellationToken).AsTask()
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
                    ? ValueTask.FromException<Response>(new InvalidOperationException("fail"))
                    : ValueTask.FromResult(new Response(9));
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

    [Fact]
    public async Task RetryRequestBehavior_WhenDelayIsNegative_TreatsDelayAsZero()
    {
        var attempts = 0;
        var behavior = new TestRetryRequestBehavior<RetryRequestMessage, Response>(
            new LambdaRequestHandler<RetryRequestMessage, Response>((_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? ValueTask.FromException<Response>(new InvalidOperationException("fail"))
                    : ValueTask.FromResult(new Response(18));
            }),
            Options.Create(
                new RetryBehaviorOptions
                {
                    MaxRetryCount = 1,
                    Delay = TimeSpan.FromMilliseconds(-5),
                }
            )
        );

        var response = await behavior.Handle(
            new RetryRequestMessage(),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, attempts);
        Assert.Equal(18, response.Value);
    }

    [Fact]
    public async Task RetryStreamBehavior_RetriesUntilSuccess()
    {
        var attempts = 0;
        var behavior = new TestRetryStreamBehavior<RetryStreamMessage, Response>(
            new LambdaStreamHandler<RetryStreamMessage, Response>((_, cancellationToken) =>
            {
                attempts++;
                return attempts == 1
                    ? ThrowingStream<Response>(
                        new InvalidOperationException("fail"),
                        cancellationToken
                    )
                    : ValuesStream([new Response(1), new Response(2)], cancellationToken);
            }),
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 1 })
        );

        var items = await ToListAsync(
            behavior.Handle(new RetryStreamMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal(2, attempts);
        Assert.Equal([1, 2], items.Select(static x => x.Value).ToArray());
    }

    [Fact]
    public async Task RetryStreamBehavior_WhenFirstAttemptSucceeds_UsesImmediateResult()
    {
        var attempts = 0;
        var behavior = new TestRetryStreamBehavior<RetryStreamMessage, Response>(
            new LambdaStreamHandler<RetryStreamMessage, Response>((_, cancellationToken) =>
            {
                attempts++;
                return ValuesStream([new Response(16), new Response(17)], cancellationToken);
            }),
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 2 })
        );

        var items = await ToListAsync(
            behavior.Handle(new RetryStreamMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal(1, attempts);
        Assert.Equal([16, 17], items.Select(static x => x.Value).ToArray());
    }

    [Fact]
    public async Task RetryStreamBehavior_WhenDisabled_DoesNotRetry()
    {
        var attempts = 0;
        var behavior = new TestRetryStreamBehavior<RetryStreamDisabledMessage, Response>(
            new LambdaStreamHandler<RetryStreamDisabledMessage, Response>((_, cancellationToken) =>
            {
                attempts++;
                return ThrowingStream<Response>(
                    new InvalidOperationException("fail"),
                    cancellationToken
                );
            }),
            Options.Create(
                new RetryBehaviorOptions
                {
                    Disabled = true,
                    MaxRetryCount = 5,
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToListAsync(
                behavior.Handle(new RetryStreamDisabledMessage(), TestContext.Current.CancellationToken)
            )
        );

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryStreamBehavior_WhenDisabled_ForwardsStreamItems()
    {
        var behavior = new TestRetryStreamBehavior<RetryStreamDisabledMessage, Response>(
            new LambdaStreamHandler<RetryStreamDisabledMessage, Response>((_, cancellationToken) =>
                ValuesStream([new Response(10), new Response(11)], cancellationToken)),
            Options.Create(
                new RetryBehaviorOptions
                {
                    Disabled = true,
                    MaxRetryCount = 5,
                }
            )
        );

        var items = await ToListAsync(
            behavior.Handle(new RetryStreamDisabledMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal([10, 11], items.Select(static x => x.Value).ToArray());
    }

    [Fact]
    public async Task RetryStreamBehavior_RetriesOperationCanceledException()
    {
        var attempts = 0;
        var behavior = new TestRetryStreamBehavior<RetryStreamMessage, Response>(
            new LambdaStreamHandler<RetryStreamMessage, Response>((_, cancellationToken) =>
            {
                attempts++;
                return attempts == 1
                    ? ThrowingStream<Response>(
                        new OperationCanceledException("canceled", innerException: null, CancellationToken.None),
                        cancellationToken
                    )
                    : ValuesStream([new Response(12)], cancellationToken);
            }),
            Options.Create(
                new RetryBehaviorOptions
                {
                    MaxRetryCount = 1,
                    Delay = TimeSpan.FromMilliseconds(1),
                }
            )
        );

        var items = await ToListAsync(
            behavior.Handle(new RetryStreamMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal(2, attempts);
        Assert.Equal([12], items.Select(static x => x.Value).ToArray());
    }

    [Fact]
    public async Task RetryStreamBehavior_WhenDelayIsNegative_TreatsDelayAsZero()
    {
        var attempts = 0;
        var behavior = new TestRetryStreamBehavior<RetryStreamMessage, Response>(
            new LambdaStreamHandler<RetryStreamMessage, Response>((_, cancellationToken) =>
            {
                attempts++;
                return attempts == 1
                    ? ThrowingStream<Response>(new InvalidOperationException("retry"), cancellationToken)
                    : ValuesStream([new Response(19)], cancellationToken);
            }),
            Options.Create(
                new RetryBehaviorOptions
                {
                    MaxRetryCount = 1,
                    Delay = TimeSpan.FromMilliseconds(-10),
                }
            )
        );

        var items = await ToListAsync(
            behavior.Handle(new RetryStreamMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal(2, attempts);
        Assert.Equal([19], items.Select(static x => x.Value).ToArray());
    }

    [Fact]
    public async Task TimeoutStreamBehavior_ThrowsTimeoutException_WhenElapsed()
    {
        var behavior = new TestTimeoutStreamBehavior<TimeoutStreamMessage, Response>(
            new LambdaStreamHandler<TimeoutStreamMessage, Response>(static (_, cancellationToken) =>
                DelayedValuesStream([new Response(1)], TimeSpan.FromMilliseconds(100), cancellationToken)),
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    StreamTimeout = TimeSpan.FromMilliseconds(10),
                }
            )
        );

        var exception = await Assert.ThrowsAsync<TimeoutException>(async () =>
            await ToListAsync(
                behavior.Handle(new TimeoutStreamMessage(), TestContext.Current.CancellationToken)
            )
        );

        Assert.Contains("Stream exceeded timeout", exception.Message);
    }

    [Fact]
    public async Task TimeoutStreamBehavior_WhenDisabled_BypassesTimeout()
    {
        var behavior = new TestTimeoutStreamBehavior<TimeoutStreamDisabledMessage, Response>(
            new LambdaStreamHandler<TimeoutStreamDisabledMessage, Response>((_, cancellationToken) =>
                ValuesStream([new Response(3), new Response(4)], cancellationToken)),
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    Disabled = true,
                    StreamTimeout = TimeSpan.FromMilliseconds(1),
                }
            )
        );

        var items = await ToListAsync(
            behavior.Handle(new TimeoutStreamDisabledMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal([3, 4], items.Select(static x => x.Value).ToArray());
    }

    [Fact]
    public async Task TimeoutStreamBehavior_ReturnsItems_WhenWithinTimeout()
    {
        var behavior = new TestTimeoutStreamBehavior<TimeoutStreamMessage, Response>(
            new LambdaStreamHandler<TimeoutStreamMessage, Response>((_, cancellationToken) =>
                ValuesStream([new Response(13), new Response(14)], cancellationToken)),
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    StreamTimeout = TimeSpan.FromSeconds(1),
                }
            )
        );

        var items = await ToListAsync(
            behavior.Handle(new TimeoutStreamMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal([13, 14], items.Select(static x => x.Value).ToArray());
    }

    [Fact]
    public async Task CircuitBreakerStreamBehavior_WhenThresholdReached_OpensCircuit()
    {
        var attempts = 0;
        var behavior = new CircuitBreakerStreamBehavior<CircuitStreamMessage, Response>(
            new LambdaStreamHandler<CircuitStreamMessage, Response>((_, cancellationToken) =>
            {
                attempts++;
                return attempts == 1
                    ? ThrowingStream<Response>(
                        new InvalidOperationException("boom"),
                        cancellationToken
                    )
                    : ValuesStream([new Response(8)], cancellationToken);
            }),
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    FailureThreshold = 1,
                    OpenDuration = TimeSpan.FromMilliseconds(100),
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToListAsync(
                behavior.Handle(new CircuitStreamMessage(), TestContext.Current.CancellationToken)
            )
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToListAsync(
                behavior.Handle(new CircuitStreamMessage(), TestContext.Current.CancellationToken)
            )
        );

        Assert.Contains("Circuit open for stream", openException.Message);
    }

    [Fact]
    public async Task CircuitBreakerStreamBehavior_WhenDisabled_BypassesCircuit()
    {
        var behavior = new CircuitBreakerStreamBehavior<CircuitStreamMessage, Response>(
            new LambdaStreamHandler<CircuitStreamMessage, Response>((_, cancellationToken) =>
                ValuesStream([new Response(6), new Response(7)], cancellationToken)),
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    Disabled = true,
                    FailureThreshold = 1,
                }
            )
        );

        var items = await ToListAsync(
            behavior.Handle(new CircuitStreamMessage(), TestContext.Current.CancellationToken)
        );

        Assert.Equal([6, 7], items.Select(static x => x.Value).ToArray());
    }

    [Fact]
    public async Task CircuitBreakerStreamBehavior_CompletesOnSuccess()
    {
        var behavior = new CircuitBreakerStreamBehavior<CircuitStreamMessage, Response>(
            new LambdaStreamHandler<CircuitStreamMessage, Response>((_, cancellationToken) =>
                ValuesStream([new Response(15)], cancellationToken)),
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        var items = await WaitUntilSucceedsAsync(() =>
            ToListAsync(behavior.Handle(new CircuitStreamMessage(), TestContext.Current.CancellationToken))
        );

        Assert.Equal([15], items.Select(static x => x.Value).ToArray());
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
        Func<TMessage, CancellationToken, ValueTask<TResponse>> callback
    ) : IRequestHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        public ValueTask<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class LambdaCommandHandler<TMessage>(
        Func<TMessage, CancellationToken, ValueTask> callback
    ) : ICommandHandler<TMessage>
        where TMessage : notnull
    {
        public ValueTask Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class LambdaNotificationHandler<TMessage>(
        Func<TMessage, CancellationToken, ValueTask> callback
    ) : INotificationHandler<TMessage>
        where TMessage : notnull
    {
        public ValueTask Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class LambdaStreamHandler<TMessage, TResponse>(
        Func<TMessage, CancellationToken, IAsyncEnumerable<TResponse>> callback
    ) : IStreamHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        public IAsyncEnumerable<TResponse> Handle(
            TMessage message,
            CancellationToken cancellationToken = default
        ) => callback(message, cancellationToken);
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

    private sealed class TestRetryStreamBehavior<TMessage, TResponse>(
        IStreamHandler<TMessage, TResponse> handler,
        IOptions<RetryBehaviorOptions> optionsAccessor
    ) : RetryStreamBehavior<TMessage, TResponse>(handler, optionsAccessor)
        where TMessage : notnull;

    private sealed class TestTimeoutStreamBehavior<TMessage, TResponse>(
        IStreamHandler<TMessage, TResponse> handler,
        IOptions<TimeoutBehaviorOptions> optionsAccessor
    ) : TimeoutStreamBehavior<TMessage, TResponse>(handler, optionsAccessor)
        where TMessage : notnull;

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> source)
    {
        var result = new List<T>();
        await foreach (var item in source.WithCancellation(TestContext.Current.CancellationToken))
        {
            result.Add(item);
        }

        return result;
    }

    private static async IAsyncEnumerable<T> ValuesStream<T>(
        IEnumerable<T> values,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var item in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return item;
        }
    }

    private static async IAsyncEnumerable<T> DelayedValuesStream<T>(
        IEnumerable<T> values,
        TimeSpan delay,
        [EnumeratorCancellation] CancellationToken cancellationToken
    )
    {
        foreach (var item in values)
        {
            await Task.Delay(delay, cancellationToken);
            yield return item;
        }
    }

    private static IAsyncEnumerable<T> ThrowingStream<T>(
        Exception exception,
        CancellationToken cancellationToken
    ) => new ThrowingAsyncEnumerable<T>(exception, cancellationToken);

    private sealed class ThrowingAsyncEnumerable<T>(
        Exception exception,
        CancellationToken cancellationToken
    ) : IAsyncEnumerable<T>, IAsyncEnumerator<T>
    {
        private bool thrown;
        public T Current => default!;

        public IAsyncEnumerator<T> GetAsyncEnumerator(
            CancellationToken cancellationToken = default
        ) => this;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public ValueTask<bool> MoveNextAsync()
        {
            if (thrown)
            {
                return ValueTask.FromResult(false);
            }

            thrown = true;
            cancellationToken.ThrowIfCancellationRequested();
            throw exception;
        }
    }
}
