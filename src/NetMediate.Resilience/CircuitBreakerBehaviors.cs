namespace NetMediate.Resilience;

/// <summary>
/// Provides a request pipeline behavior that implements the circuit breaker pattern, preventing further request
/// handling when a configurable failure threshold is exceeded within a specified time window.
/// </summary>
/// <remarks>The circuit breaker is maintained per closed generic type, isolating failure tracking for each
/// message type. When the number of consecutive failures reaches the configured threshold, the circuit opens and
/// subsequent requests are rejected for the specified duration. After the open period elapses, the circuit resets and
/// allows requests to proceed. This behavior helps prevent repeated failures from overwhelming downstream
/// systems.</remarks>
/// <typeparam name="TMessage">The type of the request message. Must implement IRequest<TResponse> and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the request handler.</typeparam>
/// <param name="options">The configuration options that control the failure threshold and open duration for the circuit breaker.</param>
public sealed class CircuitBreakerRequestBehavior<TMessage, TResponse>(
    CircuitBreakerBehaviorOptions options
) : IRequestBehavior<TMessage, TResponse> where TMessage : notnull, IRequest<TResponse>
{
    private static readonly Lock s_sync = new();
    private static int s_consecutiveFailures;
    private static DateTimeOffset? s_openUntil;

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(TMessage message, RequestHandlerDelegate<TMessage, TResponse> next, CancellationToken cancellationToken = default)
    {
        if (IsCircuitOpen())
            throw new InvalidOperationException(
                "Circuit open for request."
            );

        try
        {
            var result = await next(message, cancellationToken).ConfigureAwait(false);
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
        lock (s_sync)
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
        lock (s_sync)
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

        lock (s_sync)
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
/// Provides a notification pipeline behavior that applies a circuit breaker pattern to notification handlers,
/// temporarily blocking message processing after a configurable number of consecutive failures.
/// </summary>
/// <remarks>The circuit breaker state is maintained separately for each closed generic type, ensuring that
/// failures in one notification type do not affect others. When the number of consecutive failures reaches the
/// specified threshold, the circuit opens and blocks further notifications for the configured duration. After the open
/// period elapses, the circuit resets and allows processing to resume.</remarks>
/// <typeparam name="TMessage">The type of notification message handled by this behavior. Must implement <see cref="INotification"/> and be
/// non-nullable.</typeparam>
/// <param name="options">The configuration options that control the failure threshold and open duration for the circuit breaker.</param>
public sealed class CircuitBreakerNotificationBehavior<TMessage>(CircuitBreakerBehaviorOptions options)
    : INotificationBehavior<TMessage> where TMessage : notnull, INotification
{
    private static readonly Lock s_sync = new();
    private static int s_consecutiveFailures;
    private static DateTimeOffset? s_openUntil;

    /// <inheritdoc />
    public async ValueTask Handle(
        TMessage message,
        NotificationHandlerDelegate<TMessage> next,
        CancellationToken cancellationToken = default
    )
    {
        if (IsCircuitOpen())
            throw new InvalidOperationException(
                "Circuit open for notification."
            );

        try
        {
            await next(message, cancellationToken).ConfigureAwait(false);
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
        lock (s_sync)
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
        lock (s_sync)
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

        lock (s_sync)
        {
            s_consecutiveFailures++;
            if (s_consecutiveFailures < threshold)
                return;

            s_openUntil = DateTimeOffset.UtcNow.Add(openDuration);
            s_consecutiveFailures = 0;
        }
    }
}
