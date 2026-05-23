using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NetMediate.Diagnostics;

/// <summary>
/// Provides a base implementation of a stream handler that adds telemetry instrumentation to the handling of streamed
/// messages.
/// </summary>
/// <remarks>This abstract class wraps an existing stream handler to record telemetry data for each handled
/// request. It is intended to be used as a base for implementing custom telemetry behaviors in streaming
/// scenarios.</remarks>
/// <typeparam name="TMessage">The type of the message received by the stream handler. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response produced by the stream handler.</typeparam>
/// <param name="handler">The underlying stream handler that processes the streamed message.</param>
public abstract class TelemetryStreamBehavior<TMessage, TResponse>(IStreamHandler<TMessage, TResponse> handler) : IStreamHandler<TMessage, TResponse>
    where TMessage : notnull
{
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
            handler.Handle(message, cancellationToken),
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