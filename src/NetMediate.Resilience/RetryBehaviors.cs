namespace NetMediate.Resilience;

internal sealed class RetryRequestBehavior<TMessage, TResponse>(RetryBehaviorOptions options)
    : IPipelineRequestBehavior<TMessage, TResponse> where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken
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

internal sealed class RetryNotificationBehavior<TMessage>(RetryBehaviorOptions options) :
    IPipelineBehavior<TMessage> where TMessage : notnull
{
    /// <inheritdoc />
    public async Task Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken
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
