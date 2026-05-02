namespace NetMediate;

/// <summary>
/// Defines a handler for notification messages that do not return a result.
/// </summary>
/// <remarks>Notification handlers are typically used to process events or signals that may be handled by zero or
/// more handlers. Unlike request handlers, notification handlers do not return a value to the sender.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must implement <see cref="INotification"/> and cannot be null.</typeparam>
public interface INotificationHandler<in TMessage> : IHandler<TMessage, ValueTask>;
