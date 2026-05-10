namespace NetMediate;

/// <summary>
/// Represents a delegate that handles a message asynchronously.
/// </summary>
/// <remarks>Use this delegate to define custom asynchronous logic for processing messages as fire (and may forget [notifications]). The
/// operation may be canceled by the provided cancellationToken.</remarks>
/// <typeparam name="TMessage">The type of the message to handle.</typeparam>
/// <typeparam name="TResult">The type of the response data.</typeparam>
/// <param name="key">An optional key to distinguish this handler from others of the same interface type.</param>
/// <param name="message">The notification message to process.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
/// <returns>A TResult that represents the asynchronous handling operation.</returns>
public delegate TResult PipelineBehaviorDelegate<in TMessage, out TResult>(
    object? key,
    TMessage message,
    CancellationToken cancellationToken
)
    where TResult : notnull
    where TMessage : notnull;

/// <summary>
/// Represents a delegate that executes a sequence of handlers for a given message and returns a result.
/// </summary>
/// <typeparam name="THandler">The type of the handler to be executed.</typeparam>
/// <typeparam name="TMessage">The type of the message to be processed. Must not be null.</typeparam>
/// <typeparam name="TResult">The type of the result produced by the handlers. Must not be null.</typeparam>
/// <param name="key">An optional key that can be used to identify or correlate the execution context. May be null.</param>
/// <param name="message">The message to be processed by the handlers. Cannot be null.</param>
/// <param name="handlers">An array of handlers to execute for the given message.</param>
/// <param name="cancellationToken">A cancellation token that can be used to cancel the execution.</param>
/// <returns>The result produced by executing the handlers for the specified message.</returns>
public delegate TResult HandlerExecutionDelegate<in THandler, in TMessage, out TResult>(
    object? key,
    TMessage message,
    THandler[] handlers,
    CancellationToken cancellationToken
)
    where TMessage : notnull
    where TResult : notnull;
