using Microsoft.Extensions.DependencyInjection;
using NetMediate;

namespace NetMediate.DataDog.ILogger;

/// <summary>
/// ILogger integration extensions for DataDog-compatible scopes around NetMediate telemetry.
/// </summary>
public static class NetMediateDataDogILoggerExtensions
{
    /// <summary>
    /// Registers DataDog ILogger options used by scope-enrichment helpers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional options configuration.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddNetMediateDataDogILogger(
        this IServiceCollection services,
        Action<DataDogILoggerOptions>? configure = null,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var options = new DataDogILoggerOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        return services;
    }

    /// <summary>
    /// Opens a DataDog-compatible scope containing NetMediate and Activity correlation fields.
    /// </summary>
    /// <param name="logger">The target logger.</param>
    /// <param name="options">The DataDog ILogger options.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>An IDisposable scope.</returns>
    public static IDisposable BeginNetMediateDataDogScope(
        this Microsoft.Extensions.Logging.ILogger logger,
        DataDogILoggerOptions options,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var scope = new Dictionary<string, object?>
        {
            ["dd.service"] = options.Service,
            ["dd.env"] = options.Environment,
            ["dd.version"] = options.Version,
            ["netmediate.activity_source"] = NetMediateDiagnostics.ActivitySourceName,
            ["netmediate.meter"] = NetMediateDiagnostics.MeterName,
            ["trace_id"] = System.Diagnostics.Activity.Current?.TraceId.ToString(),
            ["span_id"] = System.Diagnostics.Activity.Current?.SpanId.ToString(),
        };

        return logger.BeginScope(scope) ?? NoopScope.Instance;
    }

    private sealed class NoopScope : IDisposable
    {
        public static readonly IDisposable Instance = new NoopScope();

        public void Dispose() { }
    }
}
