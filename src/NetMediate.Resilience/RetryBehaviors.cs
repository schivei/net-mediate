namespace NetMediate.Resilience;

/// <summary>
/// Request pipeline retry behavior.
/// </summary>
/// <typeparam name="TMessage">Request message type.</typeparam>
/// <typeparam name="TResponse">Request response type.</typeparam>
public sealed class RetryRequestBehavior<TMessage, TResponse>(RetryBehaviorOptions options)
    : IRequestBehavior<TMessage, TResponse>
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default
    )
    {
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await next(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxRetryCount)
                    throw;
            }
            catch (Exception) when (attempt < maxRetryCount)
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                throw;
            }
        }
    }
}

/// <summary>
/// Notification pipeline retry behavior.
/// </summary>
/// <typeparam name="TMessage">Notification message type.</typeparam>
public sealed class RetryNotificationBehavior<TMessage>(RetryBehaviorOptions options)
    : INotificationBehavior<TMessage>
{
    /// <inheritdoc />
    public async Task Handle(
        TMessage message,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken = default
    )
    {
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        for (var attempt = 0; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await next(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxRetryCount)
                    throw;
            }
            catch (Exception) when (attempt < maxRetryCount)
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                throw;
            }
        }
    }
}
