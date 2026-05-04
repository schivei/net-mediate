using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Diagnostics;

/// <summary>
/// Dependency injection extensions for NetMediate diagnostics.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Registers OpenTelemetry trace and metric behaviors for all NetMediate pipeline types
    /// (notifications, requests, and streams). Telemetry behaviors are registered before user
    /// behaviors so they wrap the entire pipeline.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddNetMediateDiagnostics(this IServiceCollection services)
    {
        services
            .AddSingleton(typeof(IPipelineBehavior<>), typeof(TelemetryNotificationBehavior<>))
            .AddSingleton(typeof(IPipelineRequestBehavior<,>), typeof(TelemetryRequestBehavior<,>))
            .AddSingleton(typeof(IPipelineStreamBehavior<,>), typeof(TelemetryStreamBehavior<,>));
        return services;
    }
}
