namespace NetMediate;

/// <summary>
/// Represents a message that produces a stream of responses of the specified type.
/// </summary>
/// <typeparam name="TResponse">The type of the elements returned by the stream.</typeparam>
public interface IStream<TResponse> : IStream;

/// <summary>
/// Represents a message that contains stream data for transmission or processing.
/// </summary>
/// <remarks>Implement this interface to define messages that encapsulate streaming content within
/// a messaging framework.</remarks>
public interface IStream : IMessage;
