using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;
using System.Reflection;

namespace NetMediate;

/// <summary>
/// Provides extension methods for registering NetMediate services with the dependency injection container.
/// </summary>
public static class NetMediateDI
{
    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans all loaded assemblies for handlers.
    /// </summary>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate(this IServiceCollection services) =>
        new MediatorServiceBuilder(services).MapAssemblies();

    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans the assembly containing the specified type <typeparamref name="T"/> for handlers.
    /// </summary>
    /// <typeparam name="T">A type from the assembly to scan for handlers.</typeparam>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate<T>(this IServiceCollection services) =>
        new MediatorServiceBuilder(services).MapAssembly<T>();

    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans the provided assemblies for handlers.
    /// </summary>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="assemblies">Assemblies to scan for handlers.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate(this IServiceCollection services, params Assembly[] assemblies) =>
        new MediatorServiceBuilder(services).MapAssemblies(assemblies);
}
