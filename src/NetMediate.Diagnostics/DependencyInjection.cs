using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Diagnostics;

/// <summary>
/// Dependency injection extensions for NetMediate diagnostics.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// Marker method called by the source generator when <c>NetMediate.Diagnostics</c> is referenced.
    /// Telemetry behaviors are registered per message-type by the source generator using closed-type
    /// <c>configure.RegisterBehavior&lt;&gt;()</c> calls — no open-generic registrations are used.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddNetMediateDiagnostics(this IServiceCollection services)
        => services; // Behaviors registered per-handler by the source generator.
}
