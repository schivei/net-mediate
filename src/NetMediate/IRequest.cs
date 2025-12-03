namespace NetMediate;

/// <summary>
/// Defines a request message of type <typeparamref name="TMessage"/> that expects a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TMessage"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public interface IRequest<in TMessage, TResponse> where TMessage : IRequest<TMessage, TResponse>;
