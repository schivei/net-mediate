namespace NetMediate;

/// <summary>
/// Defines a mediator for sending messages, notifications, and requests between components.
/// </summary>
public interface IMediator
{
    /// <summary>
    /// Asynchronously notifies all registered handlers of the specified message.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to notify handlers about. Must not be null.</typeparam>
    /// <param name="message">The message instance to be delivered to all handlers. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the notification operation.</param>
    /// <returns>A task that represents the asynchronous notification operation.</returns>
    Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;

    /// <summary>
    /// Notifies all subscribers of the specified message type with the provided message instance.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to notify subscribers with. Must not be null.</typeparam>
    /// <param name="key">An optional key used to scope the notification to a specific group of subscribers. If null, the notification is
    /// sent to all subscribers of the message type.</param>
    /// <param name="message">The message instance to send to subscribers. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the notification operation.</param>
    /// <returns>A task that represents the asynchronous notification operation.</returns>
    Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Asynchronously notifies recipients of a collection of messages.
    /// </summary>
    /// <typeparam name="TMessage">The type of messages to notify. Must not be null.</typeparam>
    /// <param name="messages">The collection of messages to be sent to recipients. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the notification operation.</param>
    /// <returns>A task that represents the asynchronous notification operation.</returns>
    Task Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Notifies subscribers with a collection of messages, optionally scoped by a key.
    /// </summary>
    /// <typeparam name="TMessage">The type of messages to be delivered. Must be non-nullable.</typeparam>
    /// <param name="key">An optional key used to scope the notification. If specified, only subscribers associated with this key will
    /// receive the messages. Can be null to broadcast to all subscribers.</param>
    /// <param name="messages">The collection of messages to deliver to subscribers. Cannot be null and must not contain null elements.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the notification operation.</param>
    /// <returns>A task that represents the asynchronous notification operation.</returns>
    Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Sends the specified message asynchronously using the configured transport or pipeline.
    /// </summary>
    /// <remarks>The method does not guarantee delivery or processing of the message unless the underlying
    /// transport supports such guarantees. The operation may complete before the message is fully processed by all
    /// recipients.</remarks>
    /// <typeparam name="TMessage">The type of the message to send. Must not be null.</typeparam>
    /// <param name="message">The message instance to send. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull;

    /// <summary>
    /// Sends the specified message asynchronously, optionally associating it with a key and supporting cancellation.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to send. Must not be null.</typeparam>
    /// <param name="key">An optional key that identifies the message or its destination. May be null if no key is required.</param>
    /// <param name="message">The message to send. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    Task Send<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Sends a collection of messages asynchronously.
    /// </summary>
    /// <typeparam name="TMessage">The type of messages to send. Must not be null.</typeparam>
    /// <param name="messages">The collection of messages to be sent. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    Task Send<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Sends a collection of messages asynchronously, optionally associating them with a specified key.
    /// </summary>
    /// <remarks>The behavior of message delivery and key usage may depend on the implementation. If the
    /// operation is canceled via the provided token, the returned task will be canceled.</remarks>
    /// <typeparam name="TMessage">The type of messages to send. Must be a non-nullable type.</typeparam>
    /// <param name="key">An optional key used to partition or group the messages. May be null if no key is required.</param>
    /// <param name="messages">The collection of messages to send. Cannot contain null elements.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the send operation.</param>
    /// <returns>A task that represents the asynchronous send operation.</returns>
    Task Send<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Sends a request message and asynchronously returns a response of the specified type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the request message to send. Must not be null.</typeparam>
    /// <typeparam name="TResponse">The type of the response expected from the request.</typeparam>
    /// <param name="message">The request message to send. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the request operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the response to the request message.</returns>
    Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Sends a request message and asynchronously returns a response of the specified type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the request message to send. Must not be null.</typeparam>
    /// <typeparam name="TResponse">The type of the response expected from the request.</typeparam>
    /// <param name="key">An optional key used to route or identify the request. May be null if not required by the implementation.</param>
    /// <param name="message">The request message to send. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the request operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the response to the request message.</returns>
    Task<TResponse> Request<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Initiates a server-streaming request using the specified message and returns an asynchronous sequence of
    /// responses.
    /// </summary>
    /// <remarks>The returned sequence yields response messages as they are received from the server. The
    /// operation can be cancelled by providing a cancellation token.</remarks>
    /// <typeparam name="TMessage">The type of the request message to send. Must not be null.</typeparam>
    /// <typeparam name="TResponse">The type of the response messages received from the stream.</typeparam>
    /// <param name="message">The request message to send to the server. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the streaming operation.</param>
    /// <returns>An asynchronous sequence of response messages received from the server.</returns>
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;

    /// <summary>
    /// Initiates an asynchronous streaming request using the specified message and returns a sequence of response
    /// messages as they become available.
    /// </summary>
    /// <remarks>The returned sequence is evaluated lazily and may not begin processing until enumeration
    /// starts. The caller is responsible for enumerating the sequence and handling cancellation as needed.</remarks>
    /// <typeparam name="TMessage">The type of the request message to send. Must not be null.</typeparam>
    /// <typeparam name="TResponse">The type of the response messages returned by the stream.</typeparam>
    /// <param name="key">An optional key used to route or correlate the request. May be null if not required by the implementation.</param>
    /// <param name="message">The request message to send. Cannot be null.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the streaming operation.</param>
    /// <returns>An asynchronous sequence of response messages of type TResponse. The sequence yields each response as it is
    /// received.</returns>
    IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;
}
