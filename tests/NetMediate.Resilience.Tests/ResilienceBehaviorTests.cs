using Microsoft.Extensions.Options;

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
        var behavior = new RetryRequestBehavior<RetryRequestMessage, Response>(
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 2 })
        );
        var attempts = 0;

        var response = await behavior.Handle(
            null,
            new RetryRequestMessage(),
            (_, _, _) =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<Response>(new InvalidOperationException("fail"))
                    : Task.FromResult(new Response(42));
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, attempts);
        Assert.Equal(42, response.Value);
    }

    [Fact]
    public async Task RetryRequestBehavior_WhenDisabled_DoesNotRetry()
    {
        var behavior = new RetryRequestBehavior<RetryDisabledMessage, Response>(
            Options.Create(
                new RetryBehaviorOptions
                {
                    Disabled = true,
                    MaxRetryCount = 5,
                }
            )
        );
        var attempts = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new RetryDisabledMessage(),
                (_, _, _) =>
                {
                    attempts++;
                    return Task.FromException<Response>(new InvalidOperationException("fail"));
                },
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryCommandBehavior_RetriesOperationCanceledException()
    {
        var behavior = new RetryCommandBehavior<RetryCommandMessage>(
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 1 })
        );
        var attempts = 0;

        await behavior.Handle(
            null,
            new RetryCommandMessage(),
            (_, _, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromCanceled(new CancellationToken(canceled: true))
                    : Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task RetryNotificationBehavior_RetriesUntilSuccess()
    {
        var behavior = new RetryNotificationBehavior<RetryNotificationMessage>(
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 2 })
        );
        var attempts = 0;

        await behavior.Handle(
            null,
            new RetryNotificationMessage(),
            (_, _, _) =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException(new InvalidOperationException("fail"))
                    : Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        Assert.Equal(3, attempts);
    }

    [Fact]
    public async Task TimeoutRequestBehavior_ThrowsTimeoutException_WhenElapsed()
    {
        var behavior = new TimeoutRequestBehavior<TimeoutRequestMessage, Response>(
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    RequestTimeout = TimeSpan.FromMilliseconds(10),
                }
            )
        );

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            behavior.Handle(
                null,
                new TimeoutRequestMessage(),
                async (_, _, cancellationToken) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                    return new Response(1);
                },
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("Request exceeded timeout", exception.Message);
    }

    [Fact]
    public async Task TimeoutCommandBehavior_WhenDisabled_BypassesTimeout()
    {
        var behavior = new TimeoutCommandBehavior<TimeoutCommandMessage>(
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    Disabled = true,
                    NotificationTimeout = TimeSpan.FromMilliseconds(1),
                }
            )
        );
        var called = false;

        await behavior.Handle(
            null,
            new TimeoutCommandMessage(),
            (_, _, _) =>
            {
                called = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        Assert.True(called);
    }

    [Fact]
    public async Task TimeoutNotificationBehavior_ThrowsTimeoutException_WhenElapsed()
    {
        var behavior = new TimeoutNotificationBehavior<TimeoutNotificationMessage>(
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    NotificationTimeout = TimeSpan.FromMilliseconds(10),
                }
            )
        );

        var exception = await Assert.ThrowsAsync<TimeoutException>(() =>
            behavior.Handle(
                null,
                new TimeoutNotificationMessage(),
                async (_, _, cancellationToken) =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
                },
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("Notification exceeded timeout", exception.Message);
    }

    [Fact]
    public async Task CircuitBreakerRequestBehavior_OpensAndResetsCircuit()
    {
        var behavior = new CircuitBreakerRequestBehavior<CircuitRequestMessage, Response>(
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    FailureThreshold = 1,
                    OpenDuration = TimeSpan.FromMilliseconds(100),
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new CircuitRequestMessage(),
                (_, _, _) => Task.FromException<Response>(new InvalidOperationException("boom")),
                TestContext.Current.CancellationToken
            )
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new CircuitRequestMessage(),
                (_, _, _) => Task.FromResult(new Response(1)),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("Circuit open for request", openException.Message);

        var response = await WaitUntilSucceedsAsync(
            async () =>
                await behavior.Handle(
                    null,
                    new CircuitRequestMessage(),
                    (_, _, _) => Task.FromResult(new Response(7)),
                    TestContext.Current.CancellationToken
                )
        );

        Assert.Equal(7, response.Value);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_CompletesOnSuccess()
    {
        var behavior = new CircuitBreakerCommandBehavior<CircuitCommandMessage>(
            Options.Create(new CircuitBreakerBehaviorOptions())
        );
        var called = false;

        await behavior.Handle(
            null,
            new CircuitCommandMessage(),
            (_, _, _) =>
            {
                called = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        Assert.True(called);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_WhenThresholdReached_OpensCircuit()
    {
        var behavior = new CircuitBreakerCommandBehavior<CircuitCommandMessage>(
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    FailureThreshold = 1,
                    OpenDuration = TimeSpan.FromMilliseconds(20),
                }
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new CircuitCommandMessage(),
                (_, _, _) => Task.FromException(new InvalidOperationException("boom")),
                TestContext.Current.CancellationToken
            )
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new CircuitCommandMessage(),
                (_, _, _) => Task.CompletedTask,
                TestContext.Current.CancellationToken
            )
        );

        Assert.Contains("Circuit open for command", openException.Message);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_WhenDisabled_BypassesCircuit()
    {
        var behavior = new CircuitBreakerCommandBehavior<CircuitCommandMessage>(
            Options.Create(
                new CircuitBreakerBehaviorOptions
                {
                    Disabled = true,
                    FailureThreshold = 1,
                }
            )
        );
        var called = false;

        await behavior.Handle(
            null,
            new CircuitCommandMessage(),
            (_, _, _) =>
            {
                called = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        Assert.True(called);
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_RethrowsFailures()
    {
        var behavior = new CircuitBreakerNotificationBehavior<CircuitNotificationMessage>(
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new CircuitNotificationMessage(),
                (_, _, _) => Task.FromException(new InvalidOperationException("boom")),
                TestContext.Current.CancellationToken
            )
        );
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_CompletesOnSuccess()
    {
        var behavior = new CircuitBreakerNotificationBehavior<CircuitNotificationMessage>(
            Options.Create(new CircuitBreakerBehaviorOptions())
        );
        var called = false;

        await behavior.Handle(
            null,
            new CircuitNotificationMessage(),
            (_, _, _) =>
            {
                called = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        Assert.True(called);
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
}
