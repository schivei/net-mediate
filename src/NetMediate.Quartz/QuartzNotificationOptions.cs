namespace NetMediate.Quartz;

/// <summary>
/// Configuration options for the Quartz-backed notification scheduler.
/// </summary>
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
}
