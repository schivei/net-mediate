namespace NetMediate.Internals;

internal interface INotifiable
{
    Task Notifies(INotificationPacket packet, CancellationToken cancellationToken = default);
}
