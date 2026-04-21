namespace NetMediate.DataDog.Serilog;

/// <summary>
/// Options for configuring DataDog log forwarding through Serilog.
/// </summary>
public sealed class DataDogSerilogOptions
{
    /// <summary>
    /// Gets or sets the DataDog API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the DataDog source value.
    /// </summary>
    public string Source { get; set; } = "csharp";

    /// <summary>
    /// Gets or sets the DataDog service value.
    /// </summary>
    public string Service { get; set; } = "netmediate";

    /// <summary>
    /// Gets or sets the DataDog host value.
    /// </summary>
    public string? Host { get; set; }

    /// <summary>
    /// Gets or sets additional DataDog tags.
    /// </summary>
    public string[] Tags { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the DataDog sink should be attached.
    /// </summary>
    public bool EnableSink { get; set; } = true;
}
