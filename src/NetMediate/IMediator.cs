namespace NetMediate;

/// <summary>
/// Defines a mediator for sending messages, notifications, and requests between components.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Publishes a notification message to all registered handlers.
    /// Any exceptions thrown by handlers propagate as <see cref="AggregateException"/>.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification payload.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a strongly-typed notification to all registered handlers.
    /// Any exceptions thrown by handlers propagate as <see cref="AggregateException"/>.
    /// </summary>
    /// <typeparam name="TMessage">The notification type, which must also implement <see cref="INotification{TMessage}"/>.</typeparam>
    /// <param name="notification">The notification to publish.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Notify<TMessage>(
        INotification<TMessage> notification,
        CancellationToken cancellationToken = default
    ) where TMessage : INotification<TMessage>;

    /// <summary>
    /// Publishes a collection of notification messages to all registered handlers.
    /// Any exceptions thrown by handlers propagate as <see cref="AggregateException"/>.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="messages">The notifications to publish. Null or empty is a no-op.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Publishes a collection of strongly-typed notifications to all registered handlers.
    /// Any exceptions thrown by handlers propagate as <see cref="AggregateException"/>.
    /// </summary>
    /// <typeparam name="TMessage">The notification type.</typeparam>
    /// <param name="notifications">The notifications to publish. Null or empty is a no-op.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Notify<TMessage>(
        IEnumerable<INotification<TMessage>> notifications,
        CancellationToken cancellationToken = default
    ) where TMessage : INotification<TMessage>;

    /// <summary>
    /// Sends a command message to its single registered handler.
    /// </summary>
    /// <typeparam name="TMessage">The command type.</typeparam>
    /// <param name="message">The command to send.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a strongly-typed command to its single registered handler.
    /// </summary>
    /// <typeparam name="TMessage">The command type, which must also implement <see cref="ICommand{TMessage}"/>.</typeparam>
    /// <param name="command">The command to send.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task Send<TMessage>(
        ICommand<TMessage> command,
        CancellationToken cancellationToken = default
    ) where TMessage : ICommand<TMessage>;

    /// <summary>
    /// Sends a request message to its handler and returns the response.
    /// </summary>
    /// <typeparam name="TMessage">The request type.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="message">The request to send.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a strongly-typed request to its handler and returns the response.
    /// </summary>
    /// <typeparam name="TMessage">The request type, which must also implement <see cref="IRequest{TMessage, TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The response type.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    Task<TResponse> Request<TMessage, TResponse>(
        IRequest<TMessage, TResponse> request,
        CancellationToken cancellationToken = default
    ) where TMessage : IRequest<TMessage, TResponse>;

    /// <summary>
    /// Sends a request message to its handler and receives a stream of responses.
    /// </summary>
    /// <typeparam name="TMessage">The request type.</typeparam>
    /// <typeparam name="TResponse">The response item type.</typeparam>
    /// <param name="message">The request to send.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a strongly-typed stream request to its handler and receives a stream of responses.
    /// </summary>
    /// <typeparam name="TMessage">The request type, which must also implement <see cref="IStream{TMessage, TResponse}"/>.</typeparam>
    /// <typeparam name="TResponse">The response item type.</typeparam>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Token to observe for cancellation.</param>
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        IStream<TMessage, TResponse> request,
        CancellationToken cancellationToken = default
    ) where TMessage : IStream<TMessage, TResponse>;
}
