using System.Diagnostics;

namespace NetMediate;

/// <summary>
/// Provides a base implementation of an ICommandHandler that adds telemetry instrumentation to command handling
/// operations.
/// </summary>
/// <remarks>This abstract class wraps an existing ICommandHandler to automatically record telemetry activities
/// and errors during command execution. Use this class to add consistent diagnostics and monitoring to command handling
/// logic.</remarks>
/// <typeparam name="TMessage">The type of the command message to handle. Must not be null.</typeparam>
/// <param name="handler">The underlying command Handler that processes the command message.</param>
public abstract class TelemetryCommandBehavior<TMessage> : ICommandHandler<TMessage>
    where TMessage : notnull
{
    /// <summary>
    /// Command handler for processing messages of type TMessage.
    /// </summary>
    /// <remarks>Provided by dependency injection and required to be non-null. Implementations should perform
    /// the message handling logic and conform to the component's lifetime and thread-safety expectations.</remarks>
    [Inject] public required ICommandHandler<TMessage> Handler { get; init; }

    /// <inheritdoc />
    public async ValueTask Handle(TMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Send");
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
            NetMediateDiagnostics.RecordSend<TMessage>();
        }
    }
}
