using System.ComponentModel.DataAnnotations;

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
public delegate ValueTask CommandHandlerDelegate<in TMessage>(TMessage message, CancellationToken cancellationToken);

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
public delegate ValueTask<TResponse> RequestHandlerDelegate<in TMessage, TResponse>(TMessage message, CancellationToken cancellationToken);

/// <summary>
/// Represents a delegate that handles a notification message asynchronously.
/// </summary>
/// <remarks>Use this delegate to define custom asynchronous logic for processing notification messages. The
/// operation may be canceled by the provided <paramref name="cancellationToken"/>.</remarks>
/// <typeparam name="TMessage">The type of the notification message to handle. Must implement <see cref="INotification"/> and cannot be null.</typeparam>
/// <param name="message">The notification message to process. Cannot be null.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
/// <returns>A <see cref="ValueTask"/> that represents the asynchronous handling operation.</returns>
public delegate ValueTask NotificationHandlerDelegate<in TMessage>(TMessage message, CancellationToken cancellationToken);

/// <summary>
/// Represents a method that handles a streaming request and returns an asynchronous sequence of responses.
/// </summary>
/// <typeparam name="TMessage">The type of the streaming request message. Must implement the <see cref="IStream"/> interface and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response elements produced by the stream.</typeparam>
/// <param name="message">The streaming request message to process.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the streaming operation.</param>
/// <returns>An asynchronous sequence of response elements generated in response to the streaming request.</returns>
public delegate IAsyncEnumerable<TResponse> StreamHandlerDelegate<in TMessage, out TResponse>(TMessage message, CancellationToken cancellationToken);

/// <summary>
/// Represents a delegate that handles a message asynchronously.
/// </summary>
/// <remarks>Use this delegate to define custom asynchronous logic for processing messages as fire (and may forget [notifications]). The
/// operation may be canceled by the provided <paramref name="cancellationToken"/>.</remarks>
/// <typeparam name="TMessage">The type of the message to handle.</typeparam>
/// <param name="message">The notification message to process.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
/// <returns>A <see cref="ValueTask"/> that represents the asynchronous handling operation.</returns>
public delegate ValueTask MessageHandlerDelegate<in TMessage>(TMessage message, CancellationToken cancellationToken);

/// <summary>
/// Represents a delegate that handles a message asynchronously.
/// </summary>
/// <remarks>Use this delegate to define custom asynchronous logic for processing messages as fire (and may forget [notifications]). The
/// operation may be canceled by the provided <paramref name="cancellationToken"/>.</remarks>
/// <typeparam name="TMessage">The type of the message to handle.</typeparam>
/// <typeparam name="TResult">The type of the response data.</typeparam>
/// <param name="message">The notification message to process.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
/// <returns>A <see cref="TResult"/> that represents the asynchronous handling operation.</returns>
public delegate TResult MessageHandlerDelegate<in TMessage, out TResult>(TMessage message, CancellationToken cancellationToken) where TResult : notnull;

/// <summary>
/// Represents a delegate that validates a message asynchronously
/// and returns a <see cref="ValidationResult"/> indicating the outcome of the validation.
/// </summary>
/// <typeparam name="TMessage">The type of the message to validate.</typeparam>
/// <param name="message">The message to validate.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the validation operation.</param>
/// <returns>A <see cref="ValueTask{ValidationResult}"/> that represents the asynchronous validation operation.</returns>
public delegate ValueTask<ValidationResult> MessageValidationDelegate<in TMessage>(TMessage message, CancellationToken cancellationToken);
