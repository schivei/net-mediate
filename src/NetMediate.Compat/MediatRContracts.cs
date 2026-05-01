namespace MediatR;

/// <summary>
/// Marker abstraction for MediatR request contracts.
/// </summary>
public interface IBaseRequest;

/// <summary>
/// Marker contract for request messages with no return value.
/// </summary>
public interface IRequest : IRequest<Unit>, NetMediate.ICommand;

/// <summary>
/// Marker contract for request messages with a response.
/// </summary>
/// <typeparam name="TResponse">Response type expected by the request.</typeparam>
public interface IRequest<TResponse> : IBaseRequest, NetMediate.IRequest<TResponse>;

/// <summary>
/// Marker contract for notification messages.
/// </summary>
public interface INotification : NetMediate.INotification;

/// <summary>
/// Marker contract for stream request messages.
/// </summary>
/// <typeparam name="TResponse">Response item type in the stream.</typeparam>
public interface IStreamRequest<TResponse> : IBaseRequest, NetMediate.IStream<TResponse>;

/// <summary>
/// Represents a void-like response for requests that do not return data.
/// </summary>
public readonly struct Unit : IEquatable<Unit>
{
    /// <summary>
    /// Singleton value for <see cref="Unit"/>.
    /// </summary>
    public static readonly Unit Value = default;

    /// <inheritdoc />
    public bool Equals(Unit other) => true;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Unit;

    /// <inheritdoc />
    public override int GetHashCode() => 0;

    /// <inheritdoc />
    public static bool operator ==(Unit _, Unit __) => true;

    /// <inheritdoc />
    public static bool operator !=(Unit _, Unit __) => false;

    /// <inheritdoc />
    public override string ToString() => "()";
}
