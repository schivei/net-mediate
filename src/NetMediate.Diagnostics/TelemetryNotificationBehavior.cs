using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Diagnostics;

/// <summary>
/// Provides a base notification handler that adds telemetry instrumentation to the handling of notification messages.
/// </summary>
/// <remarks>This abstract class wraps an existing notification handler to record telemetry data for each
/// notification. It starts a telemetry activity before handling the message and records the outcome, including any
/// exceptions. Use this class to add consistent telemetry to notification handling in MediatR-based
/// applications.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must not be null.</typeparam>
/// <param name="handler">The underlying notification handler that processes the notification message.</param>
[ExcludeFromCodeCoverage]
public abstract class TelemetryNotificationBehavior<TMessage>(INotificationHandler<TMessage> handler) : INotificationHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public virtual async Task Handle(TMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");
        try
        {
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
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
