using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

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
    public async Task<TResponse> Handle(
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

/// <summary>
/// Provides a base implementation of a stream Handler that adds telemetry instrumentation to the handling of streamed
/// messages.
/// </summary>
/// <remarks>This abstract class wraps an existing stream Handler to record telemetry data for each handled
/// request. It is intended to be used as a base for implementing custom telemetry behaviors in streaming
/// scenarios.</remarks>
/// <typeparam name="TMessage">The type of the message received by the stream Handler. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response produced by the stream Handler.</typeparam>
public abstract class TelemetryStreamBehavior<TMessage, TResponse> : IStreamHandler<TMessage, TResponse>
    where TMessage : notnull
{
    /// <summary>
    /// Gets the stream handler that processes messages of type <typeparamref name="TMessage"/> and produces responses
    /// of type <typeparamref name="TResponse"/>.
    /// </summary>
    /// <remarks>Assigned via dependency injection and required for correct operation. The property is
    /// init-only and should be set during object initialization. Implementations should be safe for concurrent and
    /// long-running streaming scenarios.</remarks>
    [Inject] public required IStreamHandler<TMessage, TResponse> Handler { get; init; }

    private readonly Channel<TResponse> _responseChannel = Channel.CreateUnbounded<TResponse>();

    internal readonly record struct StreamState(IAsyncEnumerable<TResponse> Messages, ChannelWriter<TResponse> Writer, CancellationToken CancellationToken);

    private static async Task ProcessMessageStream(StreamState state)
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");

        try
        {
            await foreach (var item in state.Messages.ConfigureAwait(false))
            {
                await state.Writer.WriteAsync(item, state.CancellationToken).ConfigureAwait(false);
                NetMediateDiagnostics.RecordStream<TMessage>();
            }
        }
        catch (Exception ex)
        {
            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            state.Writer.Complete(ex);
            return;
        }

        state.Writer.Complete();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var process = ProcessMessageStream(new(
            Handler.Handle(message, cancellationToken),
            _responseChannel.Writer,
            cancellationToken)
        );

        while (await _responseChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
        {
            yield return await _responseChannel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }

        await process.ConfigureAwait(false);
    }
}