namespace NetMediate;

/// <summary>
/// Defines a mediator for sending messages, notifications, and requests between components.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Publishes a notification message to all registered handlers.
    /// </summary>
    /// <typeparam name="TMessage">The type of the notification message.</typeparam>
    /// <param name="message">The notification message to publish.</param>
    /// <param name="onError">The callback to handle errors that occur during message processing.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Notify<TMessage>(TMessage message, NotificationErrorDelegate<TMessage> onError, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a collection of notification messages to all registered handlers.
    /// </summary>
    /// <typeparam name="TMessage">The type of the notification message.</typeparam>
    /// <param name="messages">The collection of notification messages to publish.</param>
    /// <param name="onError">The callback to handle errors that occur during message processing.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Notify<TMessage>(IEnumerable<TMessage> messages, NotificationErrorDelegate<TMessage> onError, CancellationToken cancellationToken = default)
    {
        if (messages is null || !messages.Any())
            return Task.CompletedTask;

        return Task.WhenAll(messages.Select(message => Notify(message, onError, cancellationToken)));
    }

    /// <summary>
    /// Publishes a notification message to all registered handlers.
    /// </summary>
    /// <typeparam name="TMessage">The type of the notification message.</typeparam>
    /// <param name="message">The notification message to publish.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) =>
        Notify(message, (_, _, _) => Task.CompletedTask, cancellationToken);

    /// <summary>
    /// Publishes a collection of notification messages to all registered handlers.
    /// </summary>
    /// <typeparam name="TMessage">The type of the notification message.</typeparam>
    /// <param name="messages">The collection of notification messages to publish.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
    {
        if (messages is null || !messages.Any())
            return Task.CompletedTask;

        return Task.WhenAll(messages.Select(message => Notify(message, cancellationToken)));
    }

    /// <summary>
    /// Sends a command message to a single handler.
    /// </summary>
    /// <typeparam name="TMessage">The type of the command message.</typeparam>
    /// <param name="message">The command message to send.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a request message to a handler and awaits a response.
    /// </summary>
    /// <typeparam name="TMessage">The type of the request message.</typeparam>
    /// <typeparam name="TResponse">The type of the response expected.</typeparam>
    /// <param name="message">The request message to send.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation, containing the response.</returns>
    Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a request message to a handler and receives a stream of responses asynchronously.
    /// </summary>
    /// <typeparam name="TMessage">The type of the request message.</typeparam>
    /// <typeparam name="TResponse">The type of the responses expected.</typeparam>
    /// <param name="message">The request message to send.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>An asynchronous stream of responses.</returns>
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    );
}
