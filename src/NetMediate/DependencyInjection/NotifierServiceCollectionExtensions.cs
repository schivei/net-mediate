using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMediate.Internals;
using System.Diagnostics.CodeAnalysis;

namespace NetMediate.DependencyInjection;

/// <summary>
/// Extension methods for registering the default in-process <see cref="INotifiable"/> implementation.
/// </summary>
[ExcludeFromCodeCoverage]
public static class NotifierServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="Notifier"/> as the <see cref="INotifiable"/> singleton only if no other
    /// <see cref="INotifiable"/> implementation has already been registered.
    /// </summary>
    /// <remarks>
    /// Call this after any custom <see cref="INotifiable"/> registration (e.g. a Quartz notifier) so
    /// that <see cref="Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton{TService, TImplementation}(IServiceCollection)"/>
    /// leaves the custom registration untouched while still providing the default in-process notifier
    /// for simpler scenarios.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection TryAddDefaultNetMediateNotifier(this IServiceCollection services)
    {
        services.TryAddSingleton<INotifiable, Notifier>();
        return services;
    }
}
