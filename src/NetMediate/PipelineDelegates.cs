namespace NetMediate;

/// <summary>
/// Represents a delegate that handles a command message asynchronously.
/// </summary>
/// <remarks>The delegate is typically used to encapsulate command handling logic in a pipeline or handler
/// infrastructure. The operation may be canceled if the provided <paramref name="cancellationToken"/> is
/// signaled.</remarks>
/// <typeparam name="TMessage">The type of the command message to handle. Must implement <see cref="ICommand"/> and cannot be null.</typeparam>
/// <param name="message">The command message to process. Cannot be null.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
/// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
public delegate ValueTask CommandHandlerDelegate<TMessage>(TMessage message, CancellationToken cancellationToken) where TMessage : notnull, ICommand;

/// <summary>
/// Represents an asynchronous handler delegate that processes a request message and returns a response.
/// </summary>
/// <remarks>This delegate is typically used to encapsulate the handling logic for a specific request type in a
/// pipeline or mediator pattern.</remarks>
/// <typeparam name="TMessage">The type of the request message to handle. Must implement the <see cref="IRequest"/> interface.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
/// <param name="message">The request message to process. Cannot be null.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
/// <returns>A ValueTask that represents the asynchronous operation. The task result contains the response to the request.</returns>
public delegate ValueTask<TResponse> RequestHandlerDelegate<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken) where TMessage : notnull, IRequest<TResponse>;

/// <summary>
/// Represents a delegate that handles a notification message asynchronously.
/// </summary>
/// <remarks>Use this delegate to define custom asynchronous logic for processing notification messages. The
/// operation may be canceled by the provided <paramref name="cancellationToken"/>.</remarks>
/// <typeparam name="TMessage">The type of the notification message to handle. Must implement <see cref="INotification"/> and cannot be null.</typeparam>
/// <param name="message">The notification message to process. Cannot be null.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
/// <returns>A <see cref="ValueTask"/> that represents the asynchronous handling operation.</returns>
public delegate ValueTask NotificationHandlerDelegate<TMessage>(TMessage message, CancellationToken cancellationToken) where TMessage : notnull, INotification;

/// <summary>
/// Represents a method that handles a streaming request and returns an asynchronous sequence of responses.
/// </summary>
/// <typeparam name="TMessage">The type of the streaming request message. Must implement the <see cref="IStream"/> interface and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response elements produced by the stream.</typeparam>
/// <param name="message">The streaming request message to process.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the streaming operation.</param>
/// <returns>An asynchronous sequence of response elements generated in response to the streaming request.</returns>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken) where TMessage : notnull, IStream<TResponse>;

/// <summary>
/// Represents a method that resolves the handler type for a given message instance.
/// </summary>
/// <remarks>This delegate is typically used message dispatching scenarios to determine the appropriate handler
/// type at runtime based on the message instance.</remarks>
/// <param name="message">The message for which to resolve the corresponding handler type. Cannot be null.</param>
/// <returns>The type of the handler that can process the specified message, or null if no suitable handler is found.</returns>
public delegate Type? HandlerResolverDelegate(IMessage message);
