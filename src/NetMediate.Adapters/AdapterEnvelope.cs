namespace NetMediate.Adapters;

/// <summary>
/// A standard envelope that wraps a notification message with metadata for delivery to external systems.
/// </summary>
/// <typeparam name="TMessage">The notification message type.</typeparam>
/// <param name="MessageId">A unique identifier for this envelope instance. Generated once on creation.</param>
/// <param name="MessageType">The CLR type name of the notification message.</param>
/// <param name="OccurredAt">The UTC timestamp when the envelope was created.</param>
/// <param name="Message">The notification message payload.</param>
public sealed record AdapterEnvelope<TMessage>(
    Guid MessageId,
    string MessageType,
    DateTimeOffset OccurredAt,
    TMessage Message
) where TMessage : notnull
{
    /// <summary>
    /// Creates a new <see cref="AdapterEnvelope{TMessage}"/> with a generated <see cref="MessageId"/> and the
    /// current UTC time as <see cref="OccurredAt"/>.
    /// </summary>
    /// <param name="message">The notification message to wrap.</param>
    /// <returns>A new envelope containing the provided message.</returns>
    public static AdapterEnvelope<TMessage> Create(TMessage message) =>
        new(Guid.NewGuid(), typeof(TMessage).Name, DateTimeOffset.UtcNow, message);
}
