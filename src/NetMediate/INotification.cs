namespace NetMediate;

/// <summary>
/// Defines a notification message that can be published to multiple handlers.
/// </summary>
/// <remarks>Implement this interface to represent messages that are intended to be broadcast to one or more
/// recipients without expecting a response. Notifications are typically used for event-driven communication within an
/// application.</remarks>
public interface INotification : IMessage;
