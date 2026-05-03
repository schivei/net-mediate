namespace NetMediate;

/// <summary>
/// Defines a notification message that can be published to multiple handlers.
/// </summary>
/// <remarks>Implement this interface to represent messages that are intended to be broadcast to one or more
/// notification handlers. Notifications are typically used for events or signals that do not require a response from
/// handlers.</remarks>
public interface INotification : IMessage;
