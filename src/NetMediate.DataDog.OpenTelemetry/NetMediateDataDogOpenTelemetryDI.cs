using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.DataDog.OpenTelemetry;

/// <summary>
/// OpenTelemetry integration extensions for publishing NetMediate traces and metrics to DataDog.
/// </summary>
public static class NetMediateDataDogOpenTelemetryDI
{
    /// <summary>
    /// Adds NetMediate and DataDog OTLP tracing/metrics configuration to the OpenTelemetry builder.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional options configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddNetMediateDataDogOpenTelemetry(
        this IServiceCollection services,
        Action<DataDogOpenTelemetryOptions>? configure = null
    )
    {
        var options = new DataDogOpenTelemetryOptions();
        configure?.Invoke(options);

        var builder = services.AddOpenTelemetry();

        builder.ConfigureResource(resource =>
            resource.AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ServiceVersion
            ).AddAttributes([new KeyValuePair<string, object>("deployment.environment", options.Environment)])
        );

        builder.WithTracing(tracing =>
        {
            tracing.AddSource(NetMediateDiagnostics.ActivitySourceName);
            tracing.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = options.OtlpEndpoint;
                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                    otlp.Headers = $"DD-API-KEY={options.ApiKey}";
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        });

        builder.WithMetrics(metrics =>
        {
            metrics.AddMeter(NetMediateDiagnostics.MeterName);
            metrics.AddOtlpExporter(otlp =>
            {
                otlp.Endpoint = options.OtlpEndpoint;
                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                    otlp.Headers = $"DD-API-KEY={options.ApiKey}";
                otlp.Protocol = OtlpExportProtocol.HttpProtobuf;
            });
        });

        return services;
    }
}
