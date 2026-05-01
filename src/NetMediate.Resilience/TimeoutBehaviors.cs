namespace NetMediate.Resilience;

/// <summary>
/// Provides a request behavior that enforces a timeout for request handling operations.
/// </summary>
/// <remarks>If the request handler does not complete within the specified timeout, a <see
/// cref="TimeoutException"/> is thrown. The timeout is not enforced if the configured duration is zero or
/// infinite.</remarks>
/// <typeparam name="TMessage">The type of the request message. Must implement <see cref="IRequest{TResponse}"/> and not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the request handler.</typeparam>
/// <param name="options">The options that configure the timeout duration for request processing.</param>
public sealed class TimeoutRequestBehavior<TMessage, TResponse>(TimeoutBehaviorOptions options)
    : IRequestBehavior<TMessage, TResponse> where TMessage : notnull, IRequest<TResponse>
{
    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TMessage, TResponse> next,
        CancellationToken cancellationToken = default
    )
    {
        var timeout = options.RequestTimeout;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return await next(message, cancellationToken).ConfigureAwait(false);

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        try
        {
            return await next(message, timeoutTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
            when (
                timeoutTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            )
        {
            throw new TimeoutException(
                $"Request exceeded timeout '{timeout}'.",
                ex
            );
        }
    }
}

/// <summary>
/// Provides a notification behavior that enforces a timeout for notification handlers.
/// </summary>
/// <remarks>If the notification handler does not complete within the specified timeout, a <see
/// cref="TimeoutException"/> is thrown. The timeout is not enforced if the configured duration is zero or
/// infinite.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must implement <see cref="INotification"/> and be non-nullable.</typeparam>
/// <param name="options">The options that configure the timeout duration for notification handling.</param>
public sealed class TimeoutNotificationBehavior<TMessage>(TimeoutBehaviorOptions options)
    : INotificationBehavior<TMessage> where TMessage : notnull, INotification
{
    /// <inheritdoc />
    public async ValueTask Handle(
        TMessage message,
        NotificationHandlerDelegate<TMessage> next,
        CancellationToken cancellationToken = default
    )
    {
        var timeout = options.NotificationTimeout;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            await next(message, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        try
        {
            await next(message, timeoutTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
            when (
                timeoutTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            )
        {
            throw new TimeoutException(
                $"Notification exceeded timeout '{timeout}'.",
                ex
            );
        }
    }
}
