namespace MediatR;

/// <summary>
/// Handles request messages with a response.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
    : NetMediate.IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>;

/// <summary>
/// Handles request messages without a response payload.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
public interface IRequestHandler<in TRequest>
    : NetMediate.ICommandHandler<TRequest>
    where TRequest : IRequest;

/// <summary>
/// Handles notification messages.
/// </summary>
/// <typeparam name="TNotification">Notification type.</typeparam>
public interface INotificationHandler<in TNotification>
    : NetMediate.INotificationHandler<TNotification>
    where TNotification : INotification;

/// <summary>
/// Handles stream request messages.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response item type.</typeparam>
public interface IStreamRequestHandler<in TRequest, TResponse>
    : NetMediate.IStreamHandler<TRequest, TResponse>
    where TRequest : IStreamRequest<TResponse>;
