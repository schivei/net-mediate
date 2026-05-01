namespace NetMediate.Resilience;

/// <summary>
/// Provides a request behavior that automatically retries a request when an exception occurs, according to the
/// specified retry options.
/// </summary>
/// <remarks>This behavior retries the request handler when an exception is thrown, up to the configured maximum
/// retry count. If a delay is specified, it waits for the given duration between retries. OperationCanceledException is
/// only retried if the cancellation was not requested via the provided CancellationToken. This behavior can be used to
/// improve resiliency for transient failures in request processing.</remarks>
/// <typeparam name="TMessage">The type of the request message. Must implement IRequest<TResponse> and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the request handler.</typeparam>
/// <param name="options">The options that configure the maximum number of retry attempts and the delay between retries.</param>
public sealed class RetryRequestBehavior<TMessage, TResponse>(RetryBehaviorOptions options)
    : IRequestBehavior<TMessage, TResponse> where TMessage : notnull, IRequest<TResponse>
{
    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TMessage, TResponse> next,
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
                return await next(message, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxRetryCount)
                    throw;

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (attempt < maxRetryCount)
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>
/// Provides a notification pipeline behavior that retries notification handlers when exceptions occur, using the
/// specified retry options.
/// </summary>
/// <remarks>This behavior retries the notification handler when an exception is thrown, up to the configured
/// maximum number of retries. If a delay is specified, it waits for the given duration between retry attempts. If the
/// cancellation token is canceled, the operation is aborted immediately. This can be used to improve resilience for
/// transient failures in notification handlers.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must implement <see cref="INotification"/> and be non-nullable.</typeparam>
/// <param name="options">The options that configure the maximum retry count and delay between retries.</param>
public sealed class RetryNotificationBehavior<TMessage>(RetryBehaviorOptions options) : INotificationBehavior<TMessage> where TMessage : notnull, INotification
{
    /// <inheritdoc />
    public async ValueTask Handle(
        TMessage message,
        NotificationHandlerDelegate<TMessage> next,
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
                await next(message, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxRetryCount)
                    throw;

                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (attempt < maxRetryCount)
            {
                if (delay > TimeSpan.Zero)
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
