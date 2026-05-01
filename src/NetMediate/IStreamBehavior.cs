namespace NetMediate;

/// <summary>
/// Defines a pipeline behavior for streaming message handlers that process messages and return asynchronous streams of
/// responses.
/// </summary>
/// <remarks>Implement this interface to add custom processing or logic to the execution pipeline for streaming
/// handlers. Behaviors can be used to add cross-cutting concerns such as logging, validation, or authorization to
/// streaming operations.</remarks>
/// <typeparam name="TMessage">The type of the streaming message being handled. Must implement <see cref="IStream{TResponse}"/> and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response elements produced by the stream.</typeparam>
public interface IStreamBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>, StreamHandlerDelegate<TMessage, TResponse>> where TMessage : notnull, IStream<TResponse>;
