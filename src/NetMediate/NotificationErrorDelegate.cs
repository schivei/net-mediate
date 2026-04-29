namespace NetMediate;

/// <summary>
/// Delegate for handling errors that occur during notification processing.
/// </summary>
/// <typeparam name="TMessage">The type of the notification message.</typeparam>
/// <param name="handlerType">The type of the handler that was processing the message.</param>
/// <param name="message">The notification message that caused the error.</param>
/// <param name="exception">The exception that occurred during processing.</param>
/// <returns></returns>
public delegate Task NotificationErrorDelegate<in TMessage>(
    Type handlerType,
    TMessage message,
    Exception exception
);
