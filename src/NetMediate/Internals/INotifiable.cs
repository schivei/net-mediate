namespace NetMediate.Internals;

internal interface INotifiable
{
    Task Notifies(INotificationPacket packet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Typed dispatch entry point called by
    /// <see cref="INotificationPacket.DispatchAsync"/> to avoid runtime reflection.
    /// </summary>
    Task NotifiesTyped<TMessage>(NotificationPacket<TMessage> packet, CancellationToken cancellationToken = default);
}
