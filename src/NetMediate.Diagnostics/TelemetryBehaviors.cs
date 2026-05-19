using System.Runtime.CompilerServices;

namespace NetMediate;

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
    public async Task Handle(TMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Send");
        try
        {
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
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
/// Provides a base notification handler that adds telemetry instrumentation to the handling of notification messages.
/// </summary>
/// <remarks>This abstract class wraps an existing notification handler to record telemetry data for each
/// notification. It starts a telemetry activity before handling the message and records the outcome, including any
/// exceptions. Use this class to add consistent telemetry to notification handling in MediatR-based
/// applications.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must not be null.</typeparam>
/// <param name="handler">The underlying notification handler that processes the message.</param>
public abstract class TelemetryNotificationBehavior<TMessage>(INotificationHandler<TMessage> handler) : INotificationHandler<TMessage>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task Handle(TMessage message, CancellationToken cancellationToken = default)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");
        try
        {
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
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
/// Provides a base class for request handlers that add telemetry instrumentation to the handling of requests.
/// </summary>
/// <remarks>This class wraps an existing request handler to automatically record telemetry data for each handled
/// request. It starts a diagnostic activity for the request, records any errors, and ensures that request metrics are
/// captured. Derive from this class to implement custom telemetry behaviors for request handling.</remarks>
/// <typeparam name="TMessage">The type of the request message to handle. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
/// <param name="handler">The underlying request handler that processes the message and produces a response.</param>
public abstract class TelemetryRequestBehavior<TMessage, TResponse>(IRequestHandler<TMessage, TResponse> handler) : IRequestHandler<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async Task<TResponse> Handle(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");
        try
        {
            return await handler.Handle(message, cancellationToken).ConfigureAwait(false);
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
/// Provides a base implementation of a stream handler that adds telemetry instrumentation to the handling of streamed
/// messages.
/// </summary>
/// <remarks>This abstract class wraps an existing stream handler to record telemetry data for each handled
/// request. It is intended to be used as a base for implementing custom telemetry behaviors in streaming
/// scenarios.</remarks>
/// <typeparam name="TMessage">The type of the message received by the stream handler. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response produced by the stream handler.</typeparam>
/// <param name="handler">The underlying stream handler that processes messages and produces responses.</param>
public abstract class TelemetryStreamBehavior<TMessage, TResponse>(IStreamHandler<TMessage, TResponse> handler) : IStreamHandler<TMessage, TResponse>
    where TMessage : notnull
{
    /// <inheritdoc />
    public async IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");

        List<TResponse> responses = [];
        try
        {
            await foreach (var item in handler.Handle(message, cancellationToken).ConfigureAwait(false))
            {
                responses.Add(item);
            }
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

        foreach (var response in responses)
        {
            yield return response;
        }
    }
}
