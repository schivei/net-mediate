namespace NetMediate.Internals;

internal interface INotificationPacket
{
    object Message { get; }

    /// <summary>
    /// Dispatches this notification packet to the mediator without reflection.
    /// Each concrete <see cref="NotificationPacket{TMessage}"/> calls the typed
    /// <see cref="INotifiable.NotifiesTyped{TMessage}"/> overload directly, so
    /// the generic type argument is resolved at JIT time rather than via
    /// <see cref="System.Reflection.MethodInfo.MakeGenericMethod"/> at runtime.
    /// </summary>
    Task DispatchAsync(INotifiable notifiable, CancellationToken cancellationToken);
}
