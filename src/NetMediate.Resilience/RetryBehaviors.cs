using Microsoft.Extensions.Options;

namespace NetMediate.Resilience;

/// <summary>
/// Request pipeline behavior that applies retry logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class RetryRequestBehavior<TMessage, TResponse>(IOptions<RetryBehaviorOptions> optionsAccessor)
    : IPipelineRequestBehavior<TMessage, TResponse> where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
    )
    {
        var options = optionsAccessor.Value;
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        var attempt = 0;
        while (attempt >= 0)
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
            finally
            {
                Interlocked.Increment(ref attempt);
            }
        }

        return default!;
    }
}

/// <summary>
/// Notification and command pipeline behavior that applies retry logic.
/// Registered per-handler by the source generator when <c>NetMediate.Resilience</c> is referenced.
/// </summary>
public sealed class RetryNotificationBehavior<TMessage>(IOptions<RetryBehaviorOptions> optionsAccessor) :
    IPipelineBehavior<TMessage> where TMessage : notnull
{
    /// <inheritdoc />
    public async Task Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
    )
    {
        var options = optionsAccessor.Value;
        var maxRetryCount = Math.Max(0, options.MaxRetryCount);
        var delay = options.Delay < TimeSpan.Zero ? TimeSpan.Zero : options.Delay;

        var attempt = 0;
        while (attempt >= 0)
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
            finally
            {
                Interlocked.Increment(ref attempt);
            }
        }
    }
}
