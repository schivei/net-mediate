using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;

namespace NetMediate;

/// <summary>
/// Provides extension methods for registering NetMediate services with the dependency injection container.
/// </summary>
[ExcludeFromCodeCoverage]
public static class NetMediateDI
{
    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans all loaded assemblies for handlers.
    /// </summary>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate(this IServiceCollection services) =>
        AddNetMediate(
            services,
            [
                .. AppDomain
                    .CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location)),
            ]
        );

    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans the assembly containing the specified type <typeparamref name="T"/> for handlers.
    /// </summary>
    /// <typeparam name="T">A type from the assembly to scan for handlers.</typeparam>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate<T>(this IServiceCollection services) =>
        AddNetMediate(services, typeof(T).Assembly);

    /// <summary>
    /// Adds NetMediate core services without assembly scanning and applies custom registration configuration.
    /// This overload is useful for source-generated handler registration to avoid reflection at startup.
    /// </summary>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="configure">The callback that performs explicit handler registrations.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate(
        this IServiceCollection services,
        Action<IMediatorServiceBuilder> configure
    )
    {
        Guard.ThrowIfNull(configure);

        var builder = AddNetMediate(services, []);
        configure(builder);
        return builder;
    }

    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans the provided assemblies for handlers.
    /// </summary>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="assemblies">Assemblies to scan for handlers.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate(
        this IServiceCollection services,
        params Assembly[] assemblies
    ) => new MediatorServiceBuilder(services).MapAssemblies(assemblies);
}
