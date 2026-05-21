using System.Diagnostics;

namespace NetMediate;

/// <summary>
/// Provides a base class for request handlers that add telemetry instrumentation to the handling of requests.
/// </summary>
/// <remarks>This class wraps an existing request Handler to automatically record telemetry data for each handled
/// request. It starts a diagnostic activity for the request, records any errors, and ensures that request metrics are
/// captured. Derive from this class to implement custom telemetry behaviors for request handling.</remarks>
/// <typeparam name="TMessage">The type of the request message to handle. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the Handler.</typeparam>
public abstract class TelemetryRequestBehavior<TMessage, TResponse> : IRequestHandler<TMessage, TResponse>
    where TMessage : notnull
{
    /// <summary>
    /// Gets the request handler that processes messages of type TMessage and produces responses of type TResponse.
    /// </summary>
    /// <remarks>Provided via dependency injection and required. Must be set during object initialization and
    /// is immutable thereafter.</remarks>
    [Inject] public required IRequestHandler<TMessage, TResponse> Handler { get; init; }

    /// <inheritdoc />
    public async ValueTask<TResponse> Handle(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");
        try
        {
            return await Handler.Handle(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordRequest<TMessage>();
        }
    }
}
