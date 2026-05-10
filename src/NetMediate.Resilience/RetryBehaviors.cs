using Microsoft.Extensions.Options;

namespace NetMediate.Resilience;

internal static class RetryBehaviorRunner
{
    private static readonly object CompletedResult = new();

    public static Task<TResponse> ExecuteAsync<TMessage, TResponse>(
        IOptions<RetryBehaviorOptions> optionsAccessor,
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
    )
        where TMessage : notnull =>
        ExecuteCoreAsync(
            optionsAccessor.Value,
            static async (state, ct) =>
                await state.Next(state.Key, state.Message, ct).ConfigureAwait(false),
            (Key: key, Message: message, Next: next),
            cancellationToken
        );

    public static async Task ExecuteAsync<TMessage>(
        IOptions<RetryBehaviorOptions> optionsAccessor,
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        _ = await ExecuteCoreAsync(
                optionsAccessor.Value,
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
        RetryBehaviorOptions options,
        Func<TState, CancellationToken, Task<TResult>> operation,
        TState state,
        CancellationToken cancellationToken
    )
    {
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        if (options.Disabled)
            return await operation(state, cancellationToken).ConfigureAwait(false);

        var attempt = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                return await operation(state, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && attempt < maxRetryCount
            )
            {
                attempt++;
                await DelayIfNeededAsync(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (attempt < maxRetryCount)
            {
                attempt++;
                await DelayIfNeededAsync(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static Task DelayIfNeededAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        delay > TimeSpan.Zero
            ? Task.Delay(delay, cancellationToken)
            : Task.CompletedTask;
}

public abstract class RetryRequestBehaviorBase<TMessage, TResponse>(
    IOptions<RetryBehaviorOptions> optionsAccessor
) where TMessage : notnull
{
    public Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
    ) =>
        RetryBehaviorRunner.ExecuteAsync(optionsAccessor, key, message, next, cancellationToken);
}

public abstract class RetryTaskBehaviorBase<TMessage>(
    IOptions<RetryBehaviorOptions> optionsAccessor
) where TMessage : notnull
{
    public Task Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    ) =>
        RetryBehaviorRunner.ExecuteAsync(optionsAccessor, key, message, next, cancellationToken);
}

/// <summary>
/// Request pipeline behavior that applies retry logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class RetryRequestBehavior<TMessage, TResponse>(
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryRequestBehaviorBase<TMessage, TResponse>(optionsAccessor),
    IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{ }

/// <summary>
/// Notification pipeline behavior that applies retry logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class RetryNotificationBehavior<TMessage>(
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryTaskBehaviorBase<TMessage>(optionsAccessor),
    IPipelineNotificationBehavior<TMessage>
    where TMessage : notnull
{ }

/// <summary>
/// Command pipeline behavior that applies retry logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class RetryCommandBehavior<TMessage>(
    IOptions<RetryBehaviorOptions> optionsAccessor
) : RetryTaskBehaviorBase<TMessage>(optionsAccessor),
    IPipelineCommandBehavior<TMessage>
    where TMessage : notnull
{ }
