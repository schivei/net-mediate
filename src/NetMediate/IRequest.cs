namespace NetMediate;

/// <summary>
/// Defines a request message that expects a response of the specified type when processed.
/// </summary>
/// <remarks>Implement this interface to represent a request that will be handled by a corresponding handler,
/// producing a response of type <typeparamref name="TResponse"/>.</remarks>
/// <typeparam name="TResponse">The type of the response returned when the request is handled.</typeparam>
public interface IRequest<TResponse> : IRequest;

/// <summary>
/// Defines a message that represents a request to be handled by a mediator or message handler.
/// </summary>
/// <remarks>Implement this interface to indicate that a message is intended to be processed as a request,
/// typically resulting in a response or action.</remarks>
public interface IRequest : IMessage;
