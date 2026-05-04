namespace NetMediate;

/// <summary>
/// Defines a handler for processing streaming messages and producing a sequence of responses asynchronously.
/// </summary>
/// <remarks>Implement this interface to handle messages that require asynchronous streaming of multiple
/// responses. The handler processes the incoming message and returns an <see cref="IAsyncEnumerable{TResponse}"/>
/// representing the response stream.</remarks>
/// <typeparam name="TMessage">The type of the streaming message to handle. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the responses produced by the handler.</typeparam>
public interface IStreamHandler<in TMessage, out TResponse> : IHandler<TMessage, IAsyncEnumerable<TResponse>> where TMessage : notnull;