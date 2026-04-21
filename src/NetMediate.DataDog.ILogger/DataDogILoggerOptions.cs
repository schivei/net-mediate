namespace NetMediate.DataDog.ILogger;

/// <summary>
/// Options used to enrich ILogger scopes with DataDog fields.
/// </summary>
public sealed class DataDogILoggerOptions
{
    /// <summary>
    /// Gets or sets the DataDog service value.
    /// </summary>
    public string Service { get; set; } = "netmediate";

    /// <summary>
    /// Gets or sets the DataDog environment value.
    /// </summary>
    public string Environment { get; set; } = "dev";

    /// <summary>
    /// Gets or sets the DataDog version value.
    /// </summary>
    public string Version { get; set; } = "unknown";
}
