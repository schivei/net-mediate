using Microsoft.Extensions.Options;

namespace NetMediate.Resilience;

/// <summary>
/// Request pipeline behavior that applies circuit-breaker logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
[ServiceOrder(int.MinValue + 1)]
public sealed class CircuitBreakerRequestBehavior<TMessage, TResponse>(
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : ACircuitBreakerBehavior<TMessage, Task<TResponse>>(optionsAccessor), IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc />
    public override async Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
    )
    {
        if (IsCircuitOpen())
            throw new InvalidOperationException("Circuit open for request.");

        try
        {
            var result = await next(key, message, cancellationToken).ConfigureAwait(false);
            RegisterSuccess();
            return result;
        }
        catch
        {
            RegisterFailure();
            throw;
        }
    }
}

/// <summary>
/// Notification and command pipeline behavior that applies circuit-breaker logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
[ServiceOrder(int.MinValue + 1)]
public sealed class CircuitBreakerNotificationBehavior<TMessage>(
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : ACircuitBreakerBehavior<TMessage, Task>(optionsAccessor), IPipelineBehavior<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public override async Task Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    )
    {
        if (IsCircuitOpen())
            throw new InvalidOperationException("Circuit open for notification.");

        try
        {
            await next(key, message, cancellationToken).ConfigureAwait(false);
            RegisterSuccess();
        }
        catch
        {
            RegisterFailure();
            throw;
        }
    }
}

public abstract class ACircuitBreakerBehavior<TMessage, TResult>(
    IOptions<CircuitBreakerBehaviorOptions> optionsAccessor
) : IPipelineBehavior<TMessage, TResult>
    where TMessage : notnull
{
    private static readonly Lock s_sync = new();
    private static int s_consecutiveFailures;
    private static DateTimeOffset? s_openUntil;

    protected static bool IsCircuitOpen()
    {
        lock (s_sync)
        {
            if (s_openUntil is null)
                return false;

            if (DateTimeOffset.UtcNow < s_openUntil.Value)
                return true;

            s_openUntil = null;
            s_consecutiveFailures = 0;
            return false;
        }
    }

    protected static void RegisterSuccess()
    {
        lock (s_sync)
        {
            s_consecutiveFailures = 0;
            s_openUntil = null;
        }
    }

    protected void RegisterFailure()
    {
        var options = optionsAccessor.Value;
        var threshold = Math.Max(1, options.FailureThreshold);
        var openDuration =
            options.OpenDuration <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : options.OpenDuration;

        RegisterFailure(threshold, openDuration);
    }

    private static void RegisterFailure(int threshold, TimeSpan openDuration)
    {
        lock (s_sync)
        {
            s_consecutiveFailures++;
            if (s_consecutiveFailures < threshold)
                return;

            s_openUntil = DateTimeOffset.UtcNow.Add(openDuration);
            s_consecutiveFailures = 0;
        }
    }

    public abstract TResult Handle(object? key, TMessage message, PipelineBehaviorDelegate<TMessage, TResult> next, CancellationToken cancellationToken);
}
