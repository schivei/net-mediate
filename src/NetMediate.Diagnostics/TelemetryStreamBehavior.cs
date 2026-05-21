using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NetMediate;

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

    private static async ValueTask ProcessMessageStream(StreamState state)
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
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
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

        try
        {
            while (await _responseChannel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_responseChannel.Reader.TryRead(out var item))
                {
                    yield return item;
                }
            }
        }
        finally
        {
            await process.ConfigureAwait(false);
        }
    }
}