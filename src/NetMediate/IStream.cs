namespace NetMediate;

/// <summary>
/// Defines a stream message of type <typeparamref name="TMessage"/> that expects a response of type <typeparamref name="TResponse"/>.
/// </summary>
/// <typeparam name="TMessage"></typeparam>
/// <typeparam name="TResponse"></typeparam>
public interface IStream<in TMessage, TResponse> where TMessage : IStream<TMessage, TResponse>;
