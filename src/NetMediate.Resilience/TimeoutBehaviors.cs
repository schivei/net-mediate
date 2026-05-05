using Microsoft.Extensions.Options;

namespace NetMediate.Resilience;

/// <summary>
/// Request pipeline behavior that applies a timeout.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
[ServiceOrder(int.MinValue + 3)]
public sealed class TimeoutRequestBehavior<TMessage, TResponse>(IOptions<TimeoutBehaviorOptions> optionsAccessor)
    : IPipelineRequestBehavior<TMessage, TResponse> where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
    )
    {
        var timeout = optionsAccessor.Value.RequestTimeout;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return await next(key, message, cancellationToken).ConfigureAwait(false);

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        try
        {
            return await next(key, message, timeoutTokenSource.Token).ConfigureAwait(false);
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
[ServiceOrder(int.MinValue + 3)]
public sealed class TimeoutNotificationBehavior<TMessage>(IOptions<TimeoutBehaviorOptions> optionsAccessor)
    : IPipelineBehavior<TMessage> where TMessage : notnull
{
    /// <inheritdoc />
    public async Task Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    )
    {
        var timeout = optionsAccessor.Value.NotificationTimeout;
        if (timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
        {
            await next(key, message, cancellationToken).ConfigureAwait(false);
            return;
        }

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        try
        {
            await next(key, message, timeoutTokenSource.Token).ConfigureAwait(false);
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
