namespace NetMediate;

/// <summary>
/// Defines a handler for processing a request message and returning a response asynchronously.
/// </summary>
/// <remarks>Implement this interface to handle specific request messages and provide a response. Handlers are
/// typically used in request/response messaging patterns to encapsulate business logic for processing
/// requests.</remarks>
/// <typeparam name="TMessage">The type of the request message to handle. Must implement <see cref="IRequest{TResponse}"/> and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the handler.</typeparam>
public interface IRequestHandler<TMessage, TResponse> : IHandler<TMessage, ValueTask<TResponse>> where TMessage : notnull, IRequest<TResponse>;
