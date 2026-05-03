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
    /// Dispatches a notification message to all provided handlers.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification message to dispatch.</param>
    /// <param name="handlers">The handlers to invoke.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the dispatch operation.</returns>
    Task DispatchNotifications<TMessage>(TMessage message, INotificationHandler<TMessage>[] handlers, CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Publishes a notification message through the notification pipeline.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification message to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the publish operation.</returns>
    Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Publishes multiple notification messages through the notification pipeline.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="messages">The notification messages to publish.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that represents the publish operation.</returns>
    Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull;
}
