using NetMediate.Internals;

namespace NetMediate.Quartz;

/// <summary>
/// Defines how notification messages are serialized and deserialized for storage in the Quartz job data map.
/// </summary>
public interface INotificationSerializer
{
    /// <summary>
    /// Serializes a notification message to a string representation suitable for storage.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The message to serialize.</param>
    /// <returns>A string representation of the message.</returns>
    string Serialize<TMessage>(TMessage message) where TMessage : notnull;

    /// <summary>
    /// Deserializes a notification message from its string representation.
    /// </summary>
    /// <param name="data">The serialized message data.</param>
    /// <param name="messageType">The target message type.</param>
    /// <returns>The deserialized message object.</returns>
    object? Deserialize(string data, Type messageType);
}
