using System.Runtime.CompilerServices;
using System.Threading.Channels;
using NetMediate.Internals;

namespace NetMediate.InternalNotifier;

/// <summary>
/// Notification provider that writes every notification into an unbounded in-process
/// <see cref="System.Threading.Channels.Channel{T}"/> for delivery by
/// <see cref="BackgroundNotificationWorker"/>.
/// </summary>
/// <remarks>
/// Registered automatically by <see cref="InternalNotifierExtensions.AddNetMediateInternalNotifier"/>.
/// </remarks>
internal sealed class ChannelNotificationProvider(ChannelWriter<INotificationPacket> writer) : INotificationProvider
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken) =>
        writer.WriteAsync(new NotificationPacket<TMessage>(message), cancellationToken);
}
