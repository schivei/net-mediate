using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Resilience;

/// <summary>
/// Configures retry behavior options.
/// </summary>
[OptionConfig]
[ExcludeFromCodeCoverage]
public sealed class RetryBehaviorOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the feature or component is disabled.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets the maximum retry count after the first attempt.
    /// </summary>
    public int MaxRetryCount { get; set; } = 2;

    /// <summary>
    /// Gets or sets the delay between retry attempts.
    /// </summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;
}

/// <summary>
/// Configures timeout behavior options.
/// </summary>
[OptionConfig]
[ExcludeFromCodeCoverage]
public sealed class TimeoutBehaviorOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the feature or component is disabled.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets the timeout for request handlers.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the timeout for stream handlers.
    /// </summary>
    public TimeSpan StreamTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the timeout for notification handlers.
    /// </summary>
    public TimeSpan NotificationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets the timeout for command handlers.
    /// </summary>
    public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// Configures circuit-breaker behavior options.
/// </summary>
[OptionConfig]
[ExcludeFromCodeCoverage]
public sealed class CircuitBreakerBehaviorOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the feature or component is disabled.
    /// </summary>
    public bool Disabled { get; set; }

    /// <summary>
    /// Gets or sets consecutive failure threshold before opening the circuit.
    /// </summary>
    public int FailureThreshold { get; set; } = 5;

    /// <summary>
    /// Gets or sets how long the circuit stays open after tripping.
    /// </summary>
    public TimeSpan OpenDuration { get; set; } = TimeSpan.FromSeconds(30);
}
