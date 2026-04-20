namespace NetMediate.Resilience;

/// <summary>
/// Request pipeline circuit-breaker behavior.
/// </summary>
/// <typeparam name="TMessage">Request message type.</typeparam>
/// <typeparam name="TResponse">Request response type.</typeparam>
public sealed class CircuitBreakerRequestBehavior<TMessage, TResponse>(
    CircuitBreakerBehaviorOptions options
) : IRequestBehavior<TMessage, TResponse>
{
    private static readonly object Sync = new();
    private static int s_consecutiveFailures;
    private static DateTimeOffset? s_openUntil;

    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default
    )
    {
        if (IsCircuitOpen())
            throw new InvalidOperationException(
                $"Circuit open for request '{typeof(TMessage).Name}'."
            );

        try
        {
            var result = await next(cancellationToken).ConfigureAwait(false);
            RegisterSuccess();
            return result;
        }
        catch
        {
            RegisterFailure(options);
            throw;
        }
    }

    private static bool IsCircuitOpen()
    {
        lock (Sync)
        {
            if (s_openUntil is null)
                return false;

            if (DateTimeOffset.UtcNow >= s_openUntil.Value)
            {
                s_openUntil = null;
                s_consecutiveFailures = 0;
                return false;
            }

            return true;
        }
    }

    private static void RegisterSuccess()
    {
        lock (Sync)
        {
            s_consecutiveFailures = 0;
            s_openUntil = null;
        }
    }

    private static void RegisterFailure(CircuitBreakerBehaviorOptions options)
    {
        var threshold = Math.Max(1, options.FailureThreshold);
        var openDuration =
            options.OpenDuration <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(1)
                : options.OpenDuration;

        lock (Sync)
        {
            s_consecutiveFailures++;
            if (s_consecutiveFailures < threshold)
                return;

            s_openUntil = DateTimeOffset.UtcNow.Add(openDuration);
            s_consecutiveFailures = 0;
        }
    }
}

/// <summary>
/// Notification pipeline circuit-breaker behavior.
/// </summary>
/// <typeparam name="TMessage">Notification message type.</typeparam>
public sealed class CircuitBreakerNotificationBehavior<TMessage>(CircuitBreakerBehaviorOptions options)
    : INotificationBehavior<TMessage>
{
    private static readonly object Sync = new();
    private static int s_consecutiveFailures;
    private static DateTimeOffset? s_openUntil;

    /// <inheritdoc />
    public async Task Handle(
        TMessage message,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken = default
    )
    {
        if (IsCircuitOpen())
            throw new InvalidOperationException(
                $"Circuit open for notification '{typeof(TMessage).Name}'."
            );

        try
        {
            await next(cancellationToken).ConfigureAwait(false);
            RegisterSuccess();
        }
        catch
        {
            RegisterFailure(options);
            throw;
        }
    }

    private static bool IsCircuitOpen()
    {
        lock (Sync)
        {
            if (s_openUntil is null)
                return false;

            if (DateTimeOffset.UtcNow >= s_openUntil.Value)
            {
                s_openUntil = null;
                s_consecutiveFailures = 0;
                return false;
            }

            return true;
        }
    }

    private static void RegisterSuccess()
    {
        lock (Sync)
        {
            s_consecutiveFailures = 0;
            s_openUntil = null;
        }
    }

    private static void RegisterFailure(CircuitBreakerBehaviorOptions options)
    {
        var threshold = Math.Max(1, options.FailureThreshold);
        var openDuration =
            options.OpenDuration <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(1)
                : options.OpenDuration;

        lock (Sync)
        {
            s_consecutiveFailures++;
            if (s_consecutiveFailures < threshold)
                return;

            s_openUntil = DateTimeOffset.UtcNow.Add(openDuration);
            s_consecutiveFailures = 0;
        }
    }
}
