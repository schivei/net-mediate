using Microsoft.Extensions.Options;

namespace NetMediate.Resilience;

/// <summary>
/// Request pipeline behavior that applies retry logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class RetryRequestBehavior<TMessage, TResponse>(
    IOptions<RetryBehaviorOptions> optionsAccessor
) : IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
    )
    {
        var options = optionsAccessor.Value;
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        var attempt = options.Disabled ? -1 : 0;
        while (attempt >= 0)
        {
            var result = await Execute(key, message, next, maxRetryCount, delay, attempt, cancellationToken).ConfigureAwait(false);

            if (result.Item2)
                return result.Item1;

            Interlocked.Increment(ref attempt);
        }

        return default;
    }

    private static async Task<(TResponse, bool)> Execute(object? key, TMessage message, PipelineBehaviorDelegate<TMessage, Task<TResponse>> next, int maxRetryCount, TimeSpan delay, int attempt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return (await next(key, message, cancellationToken).ConfigureAwait(false), true);
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

        return (default, false);
    }
}

/// <summary>
/// Notification pipeline behavior that applies retry logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class RetryNotificationBehavior<TMessage>(
    IOptions<RetryBehaviorOptions> optionsAccessor
) : IPipelineNotificationBehavior<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    )
    {
        var options = optionsAccessor.Value;
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        var attempt = options.Disabled ? -1 : 0;
        while (attempt >= 0)
        {
            await Execute(key, message, next, maxRetryCount, delay, attempt, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref attempt);
        }
    }

    private static async Task Execute(object? key, TMessage message, PipelineBehaviorDelegate<TMessage, Task> next, int maxRetryCount, TimeSpan delay, int attempt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await next(key, message, cancellationToken).ConfigureAwait(false);
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

/// <summary>
/// Command pipeline behavior that applies retry logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class RetryCommandBehavior<TMessage>(
    IOptions<RetryBehaviorOptions> optionsAccessor
) : IPipelineCommandBehavior<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task Handle(
        object? key,
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    )
    {
        var options = optionsAccessor.Value;
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        var attempt = options.Disabled ? -1 : 0;
        while (attempt >= 0)
        {
            await Execute(key, message, next, maxRetryCount, delay, attempt, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref attempt);
        }
    }

    private static async Task Execute(object? key, TMessage message, PipelineBehaviorDelegate<TMessage, Task> next, int maxRetryCount, TimeSpan delay, int attempt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            await next(key, message, cancellationToken).ConfigureAwait(false);
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
