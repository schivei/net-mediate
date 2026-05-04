using System.Data;
using Serilog;

namespace NetMediate.DataDog.Serilog;

/// <summary>
/// Serilog integration extensions for forwarding NetMediate telemetry to DataDog.
/// </summary>
public static class NetMediateDataDogSerilogExtensions
{
    /// <summary>
    /// Adds NetMediate DataDog Serilog enrichment and sink configuration.
    /// </summary>
    /// <param name="loggerConfiguration">The Serilog configuration instance.</param>
    /// <param name="configure">Optional DataDog sink options configuration.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The same logger configuration for chaining.</returns>
    public static LoggerConfiguration UseNetMediateDataDogSerilog(
        this LoggerConfiguration loggerConfiguration,
        Action<DataDogSerilogOptions>? configure = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new DataDogSerilogOptions();
        configure?.Invoke(options);

        loggerConfiguration.Enrich.WithProperty(
            "netmediate.activity_source",
            NetMediateDiagnostics.ActivitySourceName
        );
        loggerConfiguration.Enrich.WithProperty("netmediate.meter", NetMediateDiagnostics.MeterName);

        if (!options.EnableSink)
            return loggerConfiguration;

        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new NoNullAllowedException(
                "DataDog API key must be provided when EnableSink is true."
            );
        }

        return loggerConfiguration.WriteTo.DatadogLogs(
            apiKey: options.ApiKey,
            source: options.Source,
            service: options.Service,
            host: options.Host,
            tags: options.Tags
        );
    }
}
