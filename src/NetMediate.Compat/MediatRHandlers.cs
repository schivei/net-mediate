namespace MediatR;

/// <summary>
/// Handles request messages with a response.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response type.</typeparam>
public interface IRequestHandler<TRequest, TResponse>
    : NetMediate.IRequestHandler<TRequest, TResponse>
    where TRequest : notnull, IRequest<TResponse>;

/// <summary>
/// Handles request messages without a response payload.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
public interface IRequestHandler<TRequest>
    : NetMediate.ICommandHandler<TRequest>
    where TRequest : notnull, IRequest;

/// <summary>
/// Handles notification messages.
/// </summary>
/// <typeparam name="TNotification">Notification type.</typeparam>
public interface INotificationHandler<TNotification>
    : NetMediate.INotificationHandler<TNotification>
    where TNotification : notnull, INotification;

/// <summary>
/// Handles stream request messages.
/// </summary>
/// <typeparam name="TRequest">Request type.</typeparam>
/// <typeparam name="TResponse">Response item type.</typeparam>
public interface IStreamRequestHandler<TRequest, TResponse>
    : NetMediate.IStreamHandler<TRequest, TResponse>
    where TRequest : notnull, IStreamRequest<TResponse>;
