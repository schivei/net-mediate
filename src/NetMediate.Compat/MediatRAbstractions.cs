namespace MediatR;

/// <summary>
/// Defines send operations for requests and streams.
/// </summary>
public interface ISender
{
    /// <summary>
    /// Sends a request and returns its response.
    /// </summary>
    /// <typeparam name="TResponse">Expected response type.</typeparam>
    /// <param name="request">Request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response from handler.</returns>
    Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a request without response payload.
    /// </summary>
    /// <typeparam name="TRequest">Request type.</typeparam>
    /// <param name="request">Request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completion.</returns>
    Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest;

    /// <summary>
    /// Sends a request as object.
    /// </summary>
    /// <param name="request">Request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Response object when available.</returns>
    Task<object?> Send(object request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a stream request and returns an asynchronous response stream.
    /// </summary>
    /// <typeparam name="TResponse">Response item type.</typeparam>
    /// <param name="request">Request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Asynchronous stream of response items.</returns>
    IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request,
        CancellationToken cancellationToken = default
    );

    /// <summary>
    /// Sends a stream request as object.
    /// </summary>
    /// <param name="request">Request instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Asynchronous stream of object responses.</returns>
    IAsyncEnumerable<object?> CreateStream(
        object request,
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Defines publish operations for notifications.
/// </summary>
public interface IPublisher
{
    /// <summary>
    /// Publishes a notification by object instance.
    /// </summary>
    /// <param name="notification">Notification instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completion.</returns>
    Task Publish(object notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a notification by type.
    /// </summary>
    /// <typeparam name="TNotification">Notification type.</typeparam>
    /// <param name="notification">Notification instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Task completion.</returns>
    Task Publish<TNotification>(
        TNotification notification,
        CancellationToken cancellationToken = default
    ) where TNotification : INotification;
}

/// <summary>
/// Unified mediator abstraction for send and publish operations.
/// </summary>
public interface IMediator : ISender, IPublisher;
