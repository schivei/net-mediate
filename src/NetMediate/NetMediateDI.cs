using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;

namespace NetMediate;

/// <summary>
/// Provides extension methods for registering NetMediate services with the dependency injection container.
/// </summary>
public static class NetMediateDI
{
    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="MediatorConfiguration"/>
    /// for further configuration.
    /// </summary>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <returns>A <see cref="MediatorConfiguration"/> instance for additional configuration.</returns>
    public static MediatorConfiguration AddNetMediate(this IServiceCollection services) =>
        new(services);
}
