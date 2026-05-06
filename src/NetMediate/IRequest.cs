namespace NetMediate;

/// <summary>
/// Defines a request message that expects a response of the specified type when processed.
/// </summary>
/// <remarks>Implement this interface to represent a request that is sent through a mediator or messaging pipeline
/// and expects a response. The response type can be any type appropriate for the operation, including void (using Unit)
/// if no response is needed.</remarks>
/// <typeparam name="TResponse">The type of the response that will be returned when the request is handled.</typeparam>
#pragma warning disable S2326
public interface IRequest<TResponse> : IMessage;
#pragma warning restore S2326
