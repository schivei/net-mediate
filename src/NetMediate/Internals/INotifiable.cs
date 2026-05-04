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
    /// Dispatches a notification message directly to the provided handlers, bypassing the pipeline.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification message instance.</param>
    /// <param name="handlers">The resolved handlers to invoke.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when dispatch finishes.</returns>
    Task DispatchNotifications<TMessage>(TMessage message, INotificationHandler<TMessage>[] handlers, CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Publishes a single notification through the pipeline and all registered handlers.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification message instance.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when all handlers have been notified.</returns>
    Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull;

    /// <summary>
    /// Publishes each message in the sequence through the pipeline and all registered handlers.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="messages">The sequence of notification messages to publish.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when all messages have been dispatched.</returns>
    Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull;
}
