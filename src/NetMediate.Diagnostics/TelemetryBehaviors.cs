namespace NetMediate;

/// <summary>
/// Provides a pipeline behavior that records telemetry for command notifications, enabling activity tracking and error
/// reporting during command execution.
/// </summary>
/// <remarks>This behavior integrates with the pipeline to start a diagnostic activity for each command
/// notification and records telemetry data, including error status if an exception occurs. It is typically used to
/// enable distributed tracing and monitoring for command handling operations.</remarks>
/// <typeparam name="TMessage">The type of the command message to be processed. Must be non-null.</typeparam>
public sealed class TelemetryCommandBehavior<TMessage> : IPipelineCommandBehavior<TMessage>
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
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Send");
        try
        {
            await next(key, message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordSend<TMessage>();
        }
    }
}

/// <summary>
/// Provides a pipeline behavior that records telemetry for notification messages as they are processed.
/// </summary>
/// <remarks>This behavior starts a diagnostic activity for each notification and records telemetry data,
/// including error status if an exception occurs during processing. It is intended to be used within a pipeline to
/// enable observability and monitoring of notification handling.</remarks>
/// <typeparam name="TMessage">The type of the notification message to be handled. Must not be null.</typeparam>
public sealed class TelemetryNotificationBehavior<TMessage> : IPipelineNotificationBehavior<TMessage>
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
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");
        try
        {
            await next(key, message, cancellationToken).ConfigureAwait(false);
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
/// Request pipeline behavior that records OpenTelemetry traces and metrics.
/// Registered per-handler by the source generator when <c>NetMediate.Diagnostics</c> is referenced.
/// </summary>
public sealed class TelemetryRequestBehavior<TMessage, TResponse>
    : IPipelineRequestBehavior<TMessage, TResponse>
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
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");
        try
        {
            return await next(key, message, cancellationToken).ConfigureAwait(false);
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
