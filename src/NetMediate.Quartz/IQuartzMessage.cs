namespace NetMediate.Quartz;

/// <summary>
/// Represents a message used with Quartz scheduling that exposes a unique identifier for tracking and correlation.
/// </summary>
/// <remarks>The Identifier is expected to be unique and stable to support idempotency, deduplication, and
/// correlation across job executions or message handlers.</remarks>
public interface IQuartzMessage
{
    /// <summary>
    /// Gets an optional unique identifier for the message, which can be used for tracking, correlation, or idempotency purposes.
    /// </summary>
    string? Identifier { get; }

    /// <summary>
    /// Gets an optional group name for the message, which can be used for categorization or grouping of related messages.
    /// </summary>
    string? GroupName { get; }
}
