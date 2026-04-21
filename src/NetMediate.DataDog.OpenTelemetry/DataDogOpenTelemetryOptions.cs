namespace NetMediate.DataDog.OpenTelemetry;

/// <summary>
/// Options for exporting NetMediate traces and metrics to DataDog through OTLP.
/// </summary>
public sealed class DataDogOpenTelemetryOptions
{
    /// <summary>
    /// Gets or sets the logical service name reported to DataDog.
    /// </summary>
    public string ServiceName { get; set; } = "netmediate";

    /// <summary>
    /// Gets or sets the service version reported to DataDog.
    /// </summary>
    public string ServiceVersion { get; set; } = "unknown";

    /// <summary>
    /// Gets or sets the deployment environment reported to DataDog.
    /// </summary>
    public string Environment { get; set; } = "dev";

    /// <summary>
    /// Gets or sets the OTLP endpoint used by the DataDog Agent.
    /// </summary>
    public Uri OtlpEndpoint { get; set; } = new("http://localhost:4318");

    /// <summary>
    /// Gets or sets an optional DataDog API key sent through OTLP headers.
    /// </summary>
    public string? ApiKey { get; set; }
}
