namespace NetMediate;

/// <summary>
/// Defines a mediator for sending messages, notifications, and requests between components.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Publishes a notification to all registered handlers.
    /// </summary>
    /// <typeparam name="TMessage">The type of the notification message.</typeparam>
    /// <param name="notification">The notification message to publish.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Notify<TMessage>(
        TMessage notification,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull;

    /// <summary>
    /// Publishes a collection of notification messages to all registered handlers.
    /// </summary>
    /// <typeparam name="TMessage">The type of the notification message.</typeparam>
    /// <param name="messages">The collection of notification messages to publish.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull;

    /// <summary>
    /// Sends a command to all registered handlers in parallel.
    /// </summary>
    /// <remarks>
    /// All registered <see cref="ICommandHandler{TMessage}"/> implementations receive the command
    /// concurrently via <c>Task.WhenAll</c>. Use this when you want to trigger an action across
    /// multiple consumers with no return value.
    /// </remarks>
    /// <typeparam name="TMessage">The type of the command message.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Send<TMessage>(
        TMessage command,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull;

    /// <summary>
    /// Sends a command to all registered handlers in parallel.
    /// </summary>
    /// <remarks>
    /// All registered <see cref="ICommandHandler{TMessage}"/> implementations receive the command
    /// concurrently via <c>Task.WhenAll</c>. Use this when you want to trigger an action across
    /// multiple consumers with no return value.
    /// </remarks>
    /// <typeparam name="TMessage">The type of the command message.</typeparam>
    /// <param name="commands">The command to send.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Send<TMessage>(
        IEnumerable<TMessage> commands,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull;

    /// <summary>
    /// Sends a request to a handler and awaits a response.
    /// </summary>
    /// <typeparam name="TMessage">The type of the request message.</typeparam>
    /// <typeparam name="TResponse">The type of the response.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation and contains the response.</returns>
    Task<TResponse> Request<TMessage, TResponse>(
        TMessage request,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull;

    /// <summary>
    /// Sends a request to a handler and receives a stream of responses asynchronously.
    /// </summary>
    /// <typeparam name="TMessage">The type of the request message.</typeparam>
    /// <typeparam name="TResponse">The type of the response items in the stream.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the stream to complete.</param>
    /// <returns>An asynchronous stream of responses.</returns>
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage request,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull;
}
