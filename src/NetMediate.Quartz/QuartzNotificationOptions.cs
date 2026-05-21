using Quartz;

namespace NetMediate.Quartz;

/// <summary>
/// Configuration options for the Quartz-backed notification scheduler.
/// </summary>
[OptionConfig]
public sealed class QuartzNotificationOptions
{
    /// <summary>
    /// Gets or sets the Quartz group name used for NetMediate notification jobs.
    /// Defaults to <c>"NetMediate"</c>.
    /// </summary>
    public string GroupName { get; set; } = "NetMediate";

    /// <summary>
    /// Gets or sets the maximum number of times Quartz will attempt to re-fire a misfired notification job.
    /// Set to <c>-1</c> for unlimited retries (Quartz default). Defaults to <c>1</c>.
    /// </summary>
    public int MisfireRetryCount { get; set; } = 1;

    /// <summary>
    /// Gets or sets the strategy used to generate identifiers for Quartz notifications.
    /// </summary>
    /// <remarks>Defaults to QuartzNotificationIdGeneration.Auto. Choose Auto to let the system select an
    /// appropriate generation algorithm, or set a specific value to control identifier format and uniqueness.</remarks>
    public QuartzNotificationIdGeneration IdGenerationStrategy { get; set; } = QuartzNotificationIdGeneration.Auto;

    internal JobKey GenerateId<TMessage>(TMessage message, INotificationSerializer serializer) where TMessage : notnull
    {
        var id = IdGenerationStrategy switch
        {
            QuartzNotificationIdGeneration.MessageHash => GenerateMessageHashId(message, serializer),
            QuartzNotificationIdGeneration.Guid => Guid.NewGuid().ToString(),
            QuartzNotificationIdGeneration.MessageIdentifier when message is IQuartzMessage qtMsg && !string.IsNullOrWhiteSpace(qtMsg.Identifier) => qtMsg.Identifier,
            _ => GenerateAutoId(message, serializer)
        };

        id = $"{typeof(TMessage).Name}_{id}";

        if (message is IQuartzMessage qMsg && !string.IsNullOrWhiteSpace(qMsg.GroupName))
            return new(id, qMsg.GroupName);

        return new(id, GroupName);
    }

    private static string GenerateAutoId<TMessage>(TMessage message, INotificationSerializer serializer) where TMessage : notnull
    {
        if (message is IQuartzMessage qtMsg && !string.IsNullOrWhiteSpace(qtMsg.Identifier))
            return qtMsg.Identifier;

        return GenerateMessageHashId(message, serializer);
    }

    private static string GenerateMessageHashId<TMessage>(TMessage message, INotificationSerializer serializer) where TMessage : notnull
    {
        var json = serializer.Serialize(message);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hashBytes);
    }
}

/// <summary>
/// Specifies strategies for generating identifiers for scheduled notifications managed by Quartz.
/// </summary>
/// <remarks>Control how notification IDs are produced to support idempotence, deduplication, and routing. Select
/// a strategy that matches the available message metadata and desired delivery semantics.</remarks>
public enum QuartzNotificationIdGeneration
{
    /// <summary>
    /// Automatic mode that lets the runtime or caller select the appropriate behavior.
    /// </summary>
    /// <remarks>Default enum value (0). Use when no explicit option is specified or when automatic selection
    /// is desired.</remarks>
    Auto = 0,

    /// <summary>
    /// Represents a hash computed over a message's content.
    /// </summary>
    /// <remarks>Used to indicate that a field contains the cryptographic hash of the entire message,
    /// typically for integrity verification.</remarks>
    MessageHash = 1,

    /// <summary>
    /// Specifies a GUID (128-bit globally unique identifier) value.
    /// </summary>
    /// <remarks>Maps to System.Guid and is used to represent unique identifiers across systems.</remarks>
    Guid = 2,

    /// <summary>
    /// Represents a message identifier used to label a specific message type.
    /// </summary>
    /// <remarks>Used in message headers and serialization to indicate the message type; values must be unique
    /// within the message namespace.</remarks>
    MessageIdentifier = 3
}
