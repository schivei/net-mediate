namespace NetMediate;

/// <summary>
/// Defines a behavior that can be added to the processing pipeline for a message, allowing custom logic to be executed
/// before or after the main message handler.
/// </summary>
/// <remarks>Implementations of this interface can be used to add cross-cutting concerns, such as logging,
/// validation, or exception handling, to the message processing pipeline. The behavior can invoke the next delegate to
/// continue processing or short-circuit the pipeline as needed.</remarks>
/// <typeparam name="TMessage">The type of the message being processed. Must implement the IMessage interface and cannot be null.</typeparam>
public interface IPipelineBehavior<TMessage> : IPipelineBehavior<TMessage, Task> where TMessage : notnull;

/// <summary>
/// Defines a behavior that can be added to the processing pipeline for a message, allowing custom logic to be executed
/// before or after the main message handler.
/// </summary>
/// <remarks>Implementations of this interface can be used to add cross-cutting concerns, such as logging,
/// validation, or exception handling, to the message processing pipeline. The behavior can invoke the next delegate to
/// continue processing or short-circuit the pipeline as needed.</remarks>
/// <typeparam name="TMessage">The type of the message being processed. Must implement the IMessage interface and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the pipeline after processing the message.</typeparam>
public interface IPipelineRequestBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, Task<TResponse>> where TMessage : notnull;

/// <summary>
/// Defines a behavior that can be added to the processing pipeline for a message, allowing custom logic to be executed
/// before or after the main message handler.
/// </summary>
/// <remarks>Implementations of this interface can be used to add cross-cutting concerns, such as logging,
/// validation, or exception handling, to the message processing pipeline. The behavior can invoke the next delegate to
/// continue processing or short-circuit the pipeline as needed.</remarks>
/// <typeparam name="TMessage">The type of the message being processed. Must implement the IMessage interface and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the pipeline after processing the message.</typeparam>
public interface IPipelineStreamBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>> where TMessage : notnull;

/// <summary>
/// Defines a behavior that can be added to the processing pipeline for a message, allowing custom logic to be executed
/// before or after the main message handler.
/// </summary>
/// <remarks>Implementations of this interface can be used to add cross-cutting concerns, such as logging,
/// validation, or exception handling, to the message processing pipeline. The behavior can invoke the next delegate to
/// continue processing or short-circuit the pipeline as needed.</remarks>
/// <typeparam name="TMessage">The type of the message being processed. Must implement the IMessage interface and cannot be null.</typeparam>
/// <typeparam name="TResult">The type of the result returned by the pipeline after processing the message.</typeparam>
public interface IPipelineBehavior<TMessage, TResult> where TMessage : notnull where TResult : notnull
{
    /// <summary>
    /// Handles the specified message by invoking the next delegate in the processing pipeline.
    /// </summary>
    /// <param name="message">The message to be processed by the handler.</param>
    /// <param name="next">A delegate representing the next handler to invoke in the pipeline.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>The result produced by processing the message through the pipeline.</returns>
    TResult Handle(
        TMessage message,
        PipelineBehaviorDelegate<TMessage, TResult> next,
        CancellationToken cancellationToken
    );
}
