namespace NetMediate;

/// <summary>
/// Defines a pipeline behavior that can inspect, modify, or handle a request and its response within the request
/// processing pipeline.
/// </summary>
/// <remarks>Implement this interface to add custom logic before or after a request is handled, such as logging,
/// validation, or exception handling. Behaviors are executed in the order they are registered in the
/// pipeline.</remarks>
/// <typeparam name="TMessage">The type of the request message. Must implement <see cref="IRequest{TResponse}"/> and cannot be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the request handler.</typeparam>
public interface IRequestBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, ValueTask<TResponse>, RequestHandlerDelegate<TMessage, TResponse>> where TMessage : notnull, IRequest<TResponse>;
