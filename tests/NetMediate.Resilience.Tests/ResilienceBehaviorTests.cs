using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;

[assembly: ExcludeFromCodeCoverage]
[assembly: GenDICoveration(false)]

namespace NetMediate.Resilience.Tests;

public sealed class ResilienceBehaviorTests
{
    private static IServiceProvider MakeProvider<T>(T options, CountdownEvent? semaphore = null)
    {
        var services = new ServiceCollection();
        services.Clear();
        services.AddLogging();
        var configuration = new ConfigurationManager();
        configuration.AddJsonStream(new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?>
        {
            [typeof(T).Name] = options
        })));
        services.AddSingleton<IConfiguration>(configuration);

        if (semaphore != null)
            services.AddSingleton(semaphore);

        services.AddNetMediate();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task RetryRequestBehavior_RetriesUntilSuccess()
    {
        var method = nameof(RetryRequestBehavior_RetriesUntilSuccess);
        var provider = MakeProvider(new RetryBehaviorOptions { MaxRetryCount = 2 });
        var attemption = provider.GetRequiredService<Attemption>();

        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.RequestRetryRequestMessageAsync(new RetryRequestMessage(method), TestContext.Current.CancellationToken);

        Assert.Equal(3, attemption.Get(method));
        Assert.Equal(42, response.Value);
    }

    [Fact]
    public async Task RetryRequestBehavior_WhenDisabled_DoesNotRetry()
    {
        var method = nameof(RetryRequestBehavior_WhenDisabled_DoesNotRetry);
        var provider = MakeProvider(new RetryBehaviorOptions { Disabled = true, MaxRetryCount = 5 });
        var attemption = provider.GetRequiredService<Attemption>();

        var mediator = provider.GetRequiredService<IMediator>();

        var response = await Assert.ThrowsAsync<MediatorException>(async () =>
            await mediator.RequestRetryDisabledMessageAsync(new RetryDisabledMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal("fail", response.InnerException!.Message);
        Assert.Equal(1, attemption.Get(method));
    }

    [Fact]
    public async Task RetryCommandBehavior_RetriesOperationCanceledException()
    {
        var method = nameof(RetryCommandBehavior_RetriesOperationCanceledException);
        var provider = MakeProvider(new RetryBehaviorOptions { MaxRetryCount = 1 });
        var attemption = provider.GetRequiredService<Attemption>();

        var mediator = provider.GetRequiredService<IMediator>();

        await mediator.SendRetryCommandMessageAsync(new RetryCommandMessage(method), TestContext.Current.CancellationToken);

        Assert.Equal(2, attemption.Get(method));
    }

    [Fact]
    public async Task RetryNotificationBehavior_RetriesUntilSuccess()
    {
        var method = nameof(RetryNotificationBehavior_RetriesUntilSuccess);
        var sem = new CountdownEvent(3);
        var provider = MakeProvider(new RetryBehaviorOptions { MaxRetryCount = 2 }, sem);
        var attemption = provider.GetRequiredService<Attemption>();

        var mediator = provider.GetRequiredService<IMediator>();

        mediator.NotifyRetryNotificationMessage(new RetryNotificationMessage(method));

        sem.Wait(TestContext.Current.CancellationToken);

        Assert.Equal(3, attemption.Get(method));
    }

    [Fact]
    public async Task TimeoutRequestBehavior_ThrowsTimeoutException_WhenElapsed()
    {
        var method = nameof(TimeoutRequestBehavior_ThrowsTimeoutException_WhenElapsed);
        var provider = MakeProvider(new TimeoutBehaviorOptions { RequestTimeout = TimeSpan.FromMilliseconds(10) });
        var attemption = provider.GetRequiredService<Attemption>();

        var mediator = provider.GetRequiredService<IMediator>();

        var response = await Assert.ThrowsAsync<MediatorException>(async () =>
            await mediator.RequestTimeoutRequestMessageAsync(new TimeoutRequestMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.IsType<TimeoutException>(response.InnerException);
        Assert.Contains("Request exceeded timeout", response.InnerException.Message);
        Assert.Equal(0, attemption.Get(method));
    }

    [Fact]
    public async Task TimeoutCommandBehavior_WhenDisabled_BypassesTimeout()
    {
        var method = nameof(TimeoutCommandBehavior_WhenDisabled_BypassesTimeout);
        var called = false;
        var behavior = new TimeoutCommandTestTimeoutCommandBehavior(
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

        await behavior.Handle(new TimeoutCommandMessage(method), TestContext.Current.CancellationToken);
        Assert.True(called);
    }

    [Fact]
    public async Task TimeoutNotificationBehavior_ThrowsTimeoutException_WhenElapsed()
    {
        var method = nameof(TimeoutNotificationBehavior_ThrowsTimeoutException_WhenElapsed);
        var behavior = new TimeoutNotificationTestTimeoutNotificationBehavior(
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
            behavior.Handle(new TimeoutNotificationMessage(method), TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Contains("Notification exceeded timeout", exception.Message);
    }

    [Fact]
    public async Task CircuitBreakerRequestBehavior_OpensAndResetsCircuit()
    {
        var method = nameof(CircuitBreakerRequestBehavior_OpensAndResetsCircuit);
        var attempts = 0;
        var behavior = new CircuitRequestCircuitBreakerRequestBehavior(
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new CircuitRequestMessage(method), TestContext.Current.CancellationToken)
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new CircuitRequestMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Circuit open for request", openException.Message);

        var response = await WaitUntilSucceedsAsync(async () =>
            await behavior.Handle(new CircuitRequestMessage(method), TestContext.Current.CancellationToken)
        );
        Assert.Equal(7, response.Value);
    }

    [Fact]
    public async Task CircuitBreakerRequestBehavior_WhenDisabled_BypassesCircuit()
    {
        var method = nameof(CircuitBreakerRequestBehavior_WhenDisabled_BypassesCircuit);
        var behavior = new CircuitRequestCircuitBreakerRequestBehavior(
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
            new CircuitRequestMessage(method),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(5, response.Value);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_WhenThresholdReached_OpensCircuit()
    {
        var method = nameof(CircuitBreakerCommandBehavior_WhenThresholdReached_OpensCircuit);
        var attempts = 0;
        var behavior = new CircuitCommandCircuitBreakerCommandBehavior(
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new CircuitCommandMessage(method), TestContext.Current.CancellationToken)
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new CircuitCommandMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Circuit open for command", openException.Message);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_WhenOpenDurationIsNonPositive_UsesFallbackDuration()
    {
        var method = nameof(CircuitBreakerCommandBehavior_WhenOpenDurationIsNonPositive_UsesFallbackDuration);
        var attempts = 0;
        var behavior = new CircuitCommandCircuitBreakerCommandBehavior(
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new CircuitCommandMessage(method), TestContext.Current.CancellationToken)
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new CircuitCommandMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Circuit open for command", openException.Message);
    }

    [Fact]
    public async Task CircuitBreakerCommandBehavior_WhenDisabled_BypassesCircuit()
    {
        var method = nameof(CircuitBreakerCommandBehavior_WhenDisabled_BypassesCircuit);
        var called = false;
        var behavior = new CircuitCommandCircuitBreakerCommandBehavior(
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

        await behavior.Handle(new CircuitCommandMessage(method), TestContext.Current.CancellationToken);
        Assert.True(called);
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_RethrowsFailures()
    {
        var method = nameof(CircuitBreakerNotificationBehavior_RethrowsFailures);
        var behavior = new CircuitNotificationCircuitBreakerNotificationBehavior(
            new LambdaNotificationHandler<CircuitNotificationMessage>((_, _) =>
                ValueTask.FromException(new InvalidOperationException("boom"))),
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CircuitNotificationMessage(method), TestContext.Current.CancellationToken).AsTask()
        );
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_CompletesOnSuccess()
    {
        var method = nameof(CircuitBreakerNotificationBehavior_CompletesOnSuccess);
        var called = false;
        var behavior = new CircuitNotificationCircuitBreakerNotificationBehavior(
            new LambdaNotificationHandler<CircuitNotificationMessage>((_, _) =>
            {
                called = true;
                return ValueTask.CompletedTask;
            }),
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        await behavior.Handle(new CircuitNotificationMessage(method), TestContext.Current.CancellationToken);
        Assert.True(called);
    }

    [Fact]
    public async Task CircuitBreakerNotificationBehavior_WhenThresholdReached_OpensCircuit()
    {
        var method = nameof(CircuitBreakerNotificationBehavior_WhenThresholdReached_OpensCircuit);
        var attempts = 0;
        var behavior = new CircuitNotificationCircuitBreakerNotificationBehavior(
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

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new CircuitNotificationMessage(method), TestContext.Current.CancellationToken)
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await behavior.Handle(new CircuitNotificationMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Contains("Circuit open for notification", openException.Message);
    }

    [Fact]
    public async Task RetryRequestBehavior_WhenDelayIsConfigured_WaitsBeforeRetrying()
    {
        var method = nameof(RetryRequestBehavior_WhenDelayIsConfigured_WaitsBeforeRetrying);
        var attempts = 0;
        var startedAt = DateTimeOffset.UtcNow;
        var behavior = new RetryRequestTestRetryRequestBehavior(
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
            new RetryRequestMessage(method),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, attempts);
        Assert.Equal(9, response.Value);
        Assert.True(DateTimeOffset.UtcNow - startedAt >= TimeSpan.FromMilliseconds(20));
    }

    [Fact]
    public async Task RetryRequestBehavior_WhenDelayIsNegative_TreatsDelayAsZero()
    {
        var method = nameof(RetryRequestBehavior_WhenDelayIsNegative_TreatsDelayAsZero);
        var attempts = 0;
        var behavior = new RetryRequestTestRetryRequestBehavior(
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
            new RetryRequestMessage(method),
            TestContext.Current.CancellationToken
        );

        Assert.Equal(2, attempts);
        Assert.Equal(18, response.Value);
    }

    [Fact]
    public async Task RetryStreamBehavior_RetriesUntilSuccess()
    {
        var method = nameof(RetryStreamBehavior_RetriesUntilSuccess);
        var attempts = 0;
        var behavior = new RetryStreamTestRetryStreamBehavior(
            new RetryStreamLambdaStreamHandler((_, cancellationToken) =>
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
            behavior.Handle(new RetryStreamMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal(2, attempts);
        Assert.Equal([1, 2], [.. items.Select(static x => x.Value)]);
    }

    [Fact]
    public async Task RetryStreamBehavior_WhenFirstAttemptSucceeds_UsesImmediateResult()
    {
        var method = nameof(RetryStreamBehavior_WhenFirstAttemptSucceeds_UsesImmediateResult);
        var attempts = 0;
        var behavior = new RetryStreamTestRetryStreamBehavior(
            new RetryStreamLambdaStreamHandler((_, cancellationToken) =>
            {
                attempts++;
                return ValuesStream([new Response(16), new Response(17)], cancellationToken);
            }),
            Options.Create(new RetryBehaviorOptions { MaxRetryCount = 2 })
        );

        var items = await ToListAsync(
            behavior.Handle(new RetryStreamMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal(1, attempts);
        Assert.Equal([16, 17], [.. items.Select(static x => x.Value)]);
    }

    [Fact]
    public async Task RetryStreamBehavior_WhenDisabled_DoesNotRetry()
    {
        var method = nameof(RetryStreamBehavior_WhenDisabled_DoesNotRetry);
        var attempts = 0;
        var behavior = new RetryStreamDisabledTestRetryStreamBehavior(
            new RetryStreamDisabledLambdaStreamHandler((_, cancellationToken) =>
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
                behavior.Handle(new RetryStreamDisabledMessage(method), TestContext.Current.CancellationToken)
            )
        );

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task RetryStreamBehavior_WhenDisabled_ForwardsStreamItems()
    {
        var method = nameof(RetryStreamBehavior_WhenDisabled_ForwardsStreamItems);
        var behavior = new RetryStreamDisabledTestRetryStreamBehavior(
            new RetryStreamDisabledLambdaStreamHandler((_, cancellationToken) =>
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
            behavior.Handle(new RetryStreamDisabledMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal([10, 11], [.. items.Select(static x => x.Value)]);
    }

    [Fact]
    public async Task RetryStreamBehavior_RetriesOperationCanceledException()
    {
        var method = nameof(RetryStreamBehavior_RetriesOperationCanceledException);
        var attempts = 0;
        var behavior = new RetryStreamTestRetryStreamBehavior(
            new RetryStreamLambdaStreamHandler((_, cancellationToken) =>
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
            behavior.Handle(new RetryStreamMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal(2, attempts);
        Assert.Equal([12], [.. items.Select(static x => x.Value)]);
    }

    [Fact]
    public async Task RetryStreamBehavior_WhenDelayIsNegative_TreatsDelayAsZero()
    {
        var method = nameof(RetryStreamBehavior_WhenDelayIsNegative_TreatsDelayAsZero);
        var attempts = 0;
        var behavior = new RetryStreamTestRetryStreamBehavior(
            new RetryStreamLambdaStreamHandler((_, cancellationToken) =>
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
            behavior.Handle(new RetryStreamMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal(2, attempts);
        Assert.Equal([19], [.. items.Select(static x => x.Value)]);
    }

    [Fact]
    public async Task TimeoutStreamBehavior_ThrowsTimeoutException_WhenElapsed()
    {
        var method = nameof(TimeoutStreamBehavior_ThrowsTimeoutException_WhenElapsed);
        var behavior = new TimeoutStreamTestTimeoutStreamBehavior(
            new TimeoutStreamLambdaStreamHandler(static (_, cancellationToken) =>
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
                behavior.Handle(new TimeoutStreamMessage(method), TestContext.Current.CancellationToken)
            )
        );

        Assert.Contains("Stream exceeded timeout", exception.Message);
    }

    [Fact]
    public async Task TimeoutStreamBehavior_WhenDisabled_BypassesTimeout()
    {
        var method = nameof(TimeoutStreamBehavior_WhenDisabled_BypassesTimeout);
        var behavior = new TimeoutStreamDisabledTestTimeoutStreamBehavior(
            new TimeoutStreamDisabledLambdaStreamHandler((_, cancellationToken) =>
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
            behavior.Handle(new TimeoutStreamDisabledMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal([3, 4], [.. items.Select(static x => x.Value)]);
    }

    [Fact]
    public async Task TimeoutStreamBehavior_ReturnsItems_WhenWithinTimeout()
    {
        var method = nameof(TimeoutStreamBehavior_ReturnsItems_WhenWithinTimeout);
        var behavior = new TimeoutStreamTestTimeoutStreamBehavior(
            new TimeoutStreamLambdaStreamHandler((_, cancellationToken) =>
                ValuesStream([new Response(13), new Response(14)], cancellationToken)),
            Options.Create(
                new TimeoutBehaviorOptions
                {
                    StreamTimeout = TimeSpan.FromSeconds(1),
                }
            )
        );

        var items = await ToListAsync(
            behavior.Handle(new TimeoutStreamMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal([13, 14], [.. items.Select(static x => x.Value)]);
    }

    [Fact]
    public async Task CircuitBreakerStreamBehavior_WhenThresholdReached_OpensCircuit()
    {
        var method = nameof(CircuitBreakerStreamBehavior_WhenThresholdReached_OpensCircuit);
        var attempts = 0;
        var behavior = new CircuitStreamCircuitBreakerStreamBehavior(
            new CircuitStreamLambdaStreamHandler((_, cancellationToken) =>
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
                behavior.Handle(new CircuitStreamMessage(method), TestContext.Current.CancellationToken)
            )
        );

        var openException = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await ToListAsync(
                behavior.Handle(new CircuitStreamMessage(method), TestContext.Current.CancellationToken)
            )
        );

        Assert.Contains("Circuit open for stream", openException.Message);
    }

    [Fact]
    public async Task CircuitBreakerStreamBehavior_WhenDisabled_BypassesCircuit()
    {
        var method = nameof(CircuitBreakerStreamBehavior_WhenDisabled_BypassesCircuit);
        var behavior = new CircuitStreamCircuitBreakerStreamBehavior(
            new CircuitStreamLambdaStreamHandler((_, cancellationToken) =>
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
            behavior.Handle(new CircuitStreamMessage(method), TestContext.Current.CancellationToken)
        );

        Assert.Equal([6, 7], [.. items.Select(static x => x.Value)]);
    }

    [Fact]
    public async Task CircuitBreakerStreamBehavior_CompletesOnSuccess()
    {
        var method = nameof(CircuitBreakerStreamBehavior_CompletesOnSuccess);
        var behavior = new CircuitStreamCircuitBreakerStreamBehavior(
            new CircuitStreamLambdaStreamHandler((_, cancellationToken) =>
                ValuesStream([new Response(15)], cancellationToken)),
            Options.Create(new CircuitBreakerBehaviorOptions())
        );

        var items = await WaitUntilSucceedsAsync(() =>
            ToListAsync(behavior.Handle(new CircuitStreamMessage(method), TestContext.Current.CancellationToken))
        );

        Assert.Equal([15], [.. items.Select(static x => x.Value)]);
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
