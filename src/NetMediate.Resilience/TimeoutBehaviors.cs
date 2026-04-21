namespace NetMediate.Resilience;

/// <summary>
/// Request pipeline timeout behavior.
/// </summary>
/// <typeparam name="TMessage">Request message type.</typeparam>
/// <typeparam name="TResponse">Request response type.</typeparam>
public sealed class TimeoutRequestBehavior<TMessage, TResponse>(TimeoutBehaviorOptions options)
    : IRequestBehavior<TMessage, TResponse>
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TMessage message,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken = default
    )
    {
        var timeout = options.RequestTimeout;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return await next(cancellationToken).ConfigureAwait(false);

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        try
        {
            return await next(timeoutTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
            when (
                timeoutTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            )
        {
            throw new TimeoutException(
                $"Request '{typeof(TMessage).Name}' exceeded timeout '{timeout}'.",
                ex
            );
        }
    }
}

/// <summary>
/// Notification pipeline timeout behavior.
/// </summary>
/// <typeparam name="TMessage">Notification message type.</typeparam>
public sealed class TimeoutNotificationBehavior<TMessage>(TimeoutBehaviorOptions options)
    : INotificationBehavior<TMessage>
{
    /// <inheritdoc />
    public async Task Handle(
        TMessage message,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken = default
    )
    {
        var timeout = options.NotificationTimeout;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            await next(cancellationToken).ConfigureAwait(false);
            return;
        }

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        try
        {
            await next(timeoutTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
            when (
                timeoutTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested
            )
        {
            throw new TimeoutException(
                $"Notification '{typeof(TMessage).Name}' exceeded timeout '{timeout}'.",
                ex
            );
        }
    }
}
