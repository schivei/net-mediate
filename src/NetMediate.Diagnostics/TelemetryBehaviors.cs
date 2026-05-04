namespace NetMediate.Diagnostics;

/// <summary>
/// Notification pipeline behavior that records OpenTelemetry traces and metrics for notification dispatch.
/// Register via <see cref="DependencyInjection.AddNetMediateDiagnostics"/>.
/// </summary>
internal sealed class TelemetryNotificationBehavior<TMessage> : IPipelineBehavior<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task> next,
        CancellationToken cancellationToken)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");
        try
        {
            await next(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordNotify<TMessage>();
        }
    }
}

/// <summary>
/// Request pipeline behavior that records OpenTelemetry traces and metrics for request dispatch.
/// Register via <see cref="DependencyInjection.AddNetMediateDiagnostics"/>.
/// </summary>
internal sealed class TelemetryRequestBehavior<TMessage, TResponse> : IPipelineRequestBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> next,
        CancellationToken cancellationToken)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");
        try
        {
            return await next(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordRequest<TMessage>();
        }
    }
}

/// <summary>
/// Stream pipeline behavior that records OpenTelemetry traces and metrics for stream dispatch.
/// Register via <see cref="DependencyInjection.AddNetMediateDiagnostics"/>.
/// </summary>
/// <remarks>
/// The activity is scoped to the stream <em>dispatch</em> (i.e. the call to
/// <see cref="IMediator.RequestStream{TMessage,TResponse}"/>), not to the full enumeration.
/// This is intentional: <see cref="IAsyncEnumerable{T}"/> is lazy and the consumer drives
/// enumeration independently. The metric counter is also incremented at dispatch time to
/// track how many streams were started.
/// </remarks>
internal sealed class TelemetryStreamBehavior<TMessage, TResponse> : IPipelineStreamBehavior<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc />
    public IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> next,
        CancellationToken cancellationToken)
    {
        // Activity covers stream dispatch only; disposed when this method returns.
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Stream");
        try
        {
            var result = next(message, cancellationToken);
            NetMediateDiagnostics.RecordStream<TMessage>();
            return result;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
    }
}
