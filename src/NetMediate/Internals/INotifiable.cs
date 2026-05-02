namespace NetMediate.Internals;

/// <summary>
/// Defines a contract for asynchronously dispatching or publishing notification messages to all registered handlers.
/// </summary>
/// <remarks>Implementations of this interface provide mechanisms for notifying multiple handlers about events or
/// messages. The order in which handlers are invoked is not guaranteed unless explicitly documented by the
/// implementation. All notification operations are asynchronous and support cancellation via a cancellation
/// token.</remarks>
public interface INotifiable
{
    Task DispatchNotifications<TMessage>(TMessage message, INotificationHandler<TMessage>[] handlers, CancellationToken cancellationToken = default) where TMessage : notnull;

    Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull;

    Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull;
}
