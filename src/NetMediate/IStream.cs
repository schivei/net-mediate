namespace NetMediate;

/// <summary>
/// Represents a message that supports streaming of response data of a specified type.
/// </summary>
/// <typeparam name="TResponse">The type of the response elements that are streamed by this message.</typeparam>
public interface IStream<TResponse> : IMessage;
