using System.Diagnostics;

namespace NetMediate.Diagnostics;

/// <summary>
/// Provides a base implementation of an ICommandHandler that adds telemetry instrumentation to command handling
/// operations.
/// </summary>
/// <remarks>This abstract class wraps an existing ICommandHandler to automatically record telemetry activities
/// and errors during command execution. Use this class to add consistent diagnostics and monitoring to command handling
/// logic.</remarks>
/// <typeparam name="TMessage">The type of the command message to handle. Must not be null.</typeparam>
/// <param name="handler">The underlying command handler that processes the command message.</param>
public abstract class TelemetryCommandBehavior<TMessage>(ICommandHandler<TMessage> handler) : ICommandHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async ValueTask Handle(TMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Send");
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
            NetMediateDiagnostics.RecordSend<TMessage>();
        }
    }
}
