using System.Diagnostics;

namespace NetMediate;

/// <summary>
/// Provides a base notification Handler that adds telemetry instrumentation to the handling of notification messages.
/// </summary>
/// <remarks>This abstract class wraps an existing notification Handler to record telemetry data for each
/// notification. It starts a telemetry activity before handling the message and records the outcome, including any
/// exceptions. Use this class to add consistent telemetry to notification handling in MediatR-based
/// applications.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must not be null.</typeparam>
public abstract class TelemetryNotificationBehavior<TMessage> : INotificationHandler<TMessage>
    where TMessage : notnull
{
    /// <summary>
    /// Gets the notification handler for messages of type TMessage.
    /// </summary>
    /// <remarks>Injected dependency; required and init-only. Provided by the dependency injection container
    /// during initialization.</remarks>
    [Inject] public required INotificationHandler<TMessage> Handler { get; init; }

    /// <inheritdoc />
    public async ValueTask Handle(TMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");
        try
        {
            await Handler.Handle(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordNotify<TMessage>();
        }
    }
}
