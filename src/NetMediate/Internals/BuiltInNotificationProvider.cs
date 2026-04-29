using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace NetMediate.Internals;

/// <summary>
/// Default <see cref="INotificationProvider"/> implementation used when no custom provider is
/// registered.  Writes every notification packet to the in-process
/// <see cref="System.Threading.Channels.Channel{T}"/> that is drained by
/// <see cref="Workers.NotificationWorker"/>.
/// </summary>
internal sealed class BuiltInNotificationProvider(Configuration configuration) : INotificationProvider
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken) =>
        configuration.ChannelWriter.WriteAsync(
            new NotificationPacket<TMessage>(message),
            cancellationToken);
}
