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
    /// <summary>
    /// Dispatches the specified notification message to all registered handlers asynchronously.
    /// </summary>
    /// <remarks>All handlers registered for the notification type will be invoked. The operation completes
    /// when all handlers have finished processing or the operation is canceled.</remarks>
    /// <typeparam name="TMessage">The type of the notification message to dispatch.</typeparam>
    /// <param name="message">The notification message to be dispatched to handlers. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the notification dispatch operation.</param>
    /// <returns>A ValueTask that represents the asynchronous operation of dispatching the notification to all handlers.</returns>
    ValueTask DispatchNotifications<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull, INotification;

    /// <summary>
    /// Publishes a notification message to all registered handlers asynchronously.
    /// </summary>
    /// <remarks>All handlers registered for the specified notification type will be invoked. The order in
    /// which handlers are called is not guaranteed.</remarks>
    /// <typeparam name="TMessage">The type of the notification message to publish. Must implement <see cref="INotification"/> and cannot be null.</typeparam>
    /// <param name="message">The notification message to be published. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the notification operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull, INotification;

    /// <summary>
    /// Publishes a collection of notification messages to all registered handlers asynchronously.
    /// </summary>
    /// <remarks>Each message in the collection is dispatched to all appropriate handlers. Handlers are
    /// invoked asynchronously, and the method completes when all handlers have finished processing. If the operation is
    /// canceled via the cancellation token, not all messages may be delivered.</remarks>
    /// <typeparam name="TMessage">The type of notification message to publish. Must implement the INotification interface and cannot be null.</typeparam>
    /// <param name="messages">The collection of notification messages to be published. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A ValueTask that represents the asynchronous publish operation.</returns>
    ValueTask Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull, INotification;
}
