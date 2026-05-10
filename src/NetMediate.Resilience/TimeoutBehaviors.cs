using Microsoft.Extensions.Options;

namespace NetMediate.Resilience;

internal static class TimeoutBehaviorRunner
{
    private static readonly object CompletedResult = new();

    public static Task<TResponse> ExecuteAsync<TMessage, TResponse>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
    )
        where TMessage : notnull =>
        ExecuteCoreAsync(
            timeout,
            disabled,
            operationName,
            static async (state, ct) =>
                await state.Next(state.Key, state.Message, ct).ConfigureAwait(false),
            (Key: key, Message: message, Next: next),
            cancellationToken
        );

    public static async Task ExecuteAsync<TMessage>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        _ = await ExecuteCoreAsync(
                timeout,
                disabled,
                operationName,
                static async (state, ct) =>
                {
                    await state.Next(state.Key, state.Message, ct).ConfigureAwait(false);
                    return CompletedResult;
                },
                (Key: key, Message: message, Next: next),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private static async Task<TResult> ExecuteCoreAsync<TState, TResult>(
        TimeSpan timeout,
        bool disabled,
        string operationName,
        Func<TState, CancellationToken, Task<TResult>> operation,
        TState state,
        CancellationToken cancellationToken
    )
    {
        if (disabled || timeout <= TimeSpan.Zero || timeout == Timeout.InfiniteTimeSpan)
            return await operation(state, cancellationToken).ConfigureAwait(false);

        using var timeoutTokenSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken
        );
        timeoutTokenSource.CancelAfter(timeout);

        try
        {
            return await operation(state, timeoutTokenSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
            when (timeoutTokenSource.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{operationName} exceeded timeout '{timeout}'.", ex);
        }
    }
}

public abstract class TimeoutRequestBehaviorBase<TMessage, TResponse>(
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) where TMessage : notnull
{
    public Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
    ) =>
        TimeoutBehaviorRunner.ExecuteAsync(
            optionsAccessor.Value.RequestTimeout,
            optionsAccessor.Value.Disabled,
            "Request",
            key,
            message,
            next,
            cancellationToken
        );
}

public abstract class TimeoutTaskBehaviorBase<TMessage>(
    IOptions<TimeoutBehaviorOptions> optionsAccessor,
    Func<TimeoutBehaviorOptions, TimeSpan> timeoutSelector,
    string operationName
) where TMessage : notnull
{
    public Task Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    ) =>
        TimeoutBehaviorRunner.ExecuteAsync(
            timeoutSelector(optionsAccessor.Value),
            optionsAccessor.Value.Disabled,
            operationName,
            key,
            message,
            next,
            cancellationToken
        );
}

/// <summary>
/// Request pipeline behavior that applies a timeout.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class TimeoutRequestBehavior<TMessage, TResponse>(
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TimeoutRequestBehaviorBase<TMessage, TResponse>(optionsAccessor),
    IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{ }

/// <summary>
/// Notification pipeline behavior that applies a timeout.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class TimeoutNotificationBehavior<TMessage>(
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TimeoutTaskBehaviorBase<TMessage>(
        optionsAccessor,
        static options => options.NotificationTimeout,
        "Notification"
    ),
    IPipelineNotificationBehavior<TMessage>
    where TMessage : notnull
{ }

/// <summary>
/// Notification pipeline behavior that applies a timeout.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class TimeoutCommandBehavior<TMessage>(
    IOptions<TimeoutBehaviorOptions> optionsAccessor
) : TimeoutTaskBehaviorBase<TMessage>(
        optionsAccessor,
        static options => options.NotificationTimeout,
        "Command"
    ),
    IPipelineCommandBehavior<TMessage>
    where TMessage : notnull
{ }
