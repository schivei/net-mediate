namespace NetMediate.Resilience;

/// <summary>
/// Request pipeline behavior that applies a timeout.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class TimeoutRequestBehavior<TMessage, TResponse>(TimeoutBehaviorOptions options)
    : IPipelineRequestBehavior<TMessage, TResponse> where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
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
/// Notification and command pipeline behavior that applies a timeout.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class TimeoutNotificationBehavior<TMessage>(TimeoutBehaviorOptions options)
    : IPipelineBehavior<TMessage> where TMessage : notnull
{
    /// <inheritdoc />
    public async Task Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
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
