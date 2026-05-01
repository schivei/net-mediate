using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NetMediate;

namespace MediatR;

/// <summary>
/// Registers MediatR-compatible abstractions backed by NetMediate.
/// </summary>
public static class MediatRServiceCollectionExtensions
{
    /// <summary>
    /// Adds MediatR-compatible services by scanning the provided assemblies.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="assemblies">Assemblies containing handlers.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddMediatR(
        this IServiceCollection services,
        params Assembly[] assemblies
    )
    {
        Guard.ThrowIfNull(services);
        Guard.ThrowIfNull(assemblies);

        services.AddNetMediate(assemblies);
        services.TryAddSingleton<IMediator, MediatorAdapter>();
        services.TryAddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.TryAddSingleton<ISender>(sp => sp.GetRequiredService<IMediator>());
        services.TryAddSingleton<IPublisher>(sp => sp.GetRequiredService<IMediator>());

        return services;
    }

    /// <summary>
    /// Adds MediatR-compatible services using a configuration callback.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="configuration">Configuration callback.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddMediatR(
        this IServiceCollection services,
        Action<MediatRServiceConfiguration> configuration
    )
    {
        Guard.ThrowIfNull(services);
        Guard.ThrowIfNull(configuration);

        var options = new MediatRServiceConfiguration();
        configuration(options);

        return services.AddMediatR([.. options.AssembliesToRegister]);
    }
}
