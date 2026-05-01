namespace NetMediate;

/// <summary>
/// Defines a pipeline behavior that is executed as part of the notification handling process for a specific
/// notification type.
/// </summary>
/// <remarks>Implement this interface to add custom logic that runs before or after notification handlers are
/// invoked. Behaviors can be used for cross-cutting concerns such as logging, validation, or instrumentation in the
/// notification pipeline.</remarks>
/// <typeparam name="TMessage">The type of notification message handled by this behavior. Must implement the INotification interface and cannot be
/// null.</typeparam>
public interface INotificationBehavior<TMessage> : IPipelineBehavior<TMessage, ValueTask, NotificationHandlerDelegate<TMessage>> where TMessage : notnull, INotification;
