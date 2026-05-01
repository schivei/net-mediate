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
        NetMediate<Notifier>(
            services,
            []
        );

    /// <summary>
    /// Adds NetMediate services to the specified service collection using the provided notifier type.
    /// </summary>
    /// <typeparam name="TNotifier">The type that implements the notification interface to be used for notifications.</typeparam>
    /// <param name="services">The service collection to which NetMediate services will be added.</param>
    /// <returns>An IMediatorServiceBuilder that can be used to further configure NetMediate services.</returns>
    public static IMediatorServiceBuilder AddNetMediate<TNotifier>(this IServiceCollection services)
        where TNotifier : class, INotifiable =>
        NetMediate<TNotifier>(
            services,
            []
        );

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

        var builder = NetMediate<Notifier>(services, []);
        configure(builder);
        return builder;
    }

    /// <summary>
    /// </summary>
    /// <remarks>This method registers the required NetMediate services and provides a hook for additional
    /// configuration. The generic type parameter allows customization of the notification handling
    /// implementation.</remarks>
    /// <typeparam name="TNotifier">The type that implements the notification interface and will be used for notification handling.</typeparam>
    /// <param name="services">The service collection to which NetMediate services will be added.</param>
    /// <param name="configure">A delegate that configures the mediator service builder after the core services have been registered. Cannot be
    /// null.</param>
    /// <returns>An instance of IMediatorServiceBuilder that can be used to further configure mediator services.</returns>
    public static IMediatorServiceBuilder AddNetMediate<TNotifier>(
        this IServiceCollection services,
        Action<IMediatorServiceBuilder> configure
    ) where TNotifier : class, INotifiable
    {
        Guard.ThrowIfNull(configure);

        var builder = NetMediate<TNotifier>(services, []);
        configure(builder);
        return builder;
    }

    /// <summary>
    /// Adds NetMediate mediator services to the specified service collection and registers handlers from the provided
    /// assemblies.
    /// </summary>
    /// <remarks>Use this method to enable mediator-based request and notification handling in your
    /// application. All handler types found in the specified assemblies will be registered with the dependency
    /// injection container.</remarks>
    /// <param name="services">The service collection to which the mediator services will be added. Cannot be null.</param>
    /// <param name="assemblies">An array of assemblies to scan for handler implementations. At least one assembly must be provided.</param>
    /// <returns>An IMediatorServiceBuilder that can be used to further configure mediator services.</returns>
    public static IMediatorServiceBuilder AddNetMediate(
        this IServiceCollection services,
        params Assembly[] assemblies
    ) => NetMediate<Notifier>(services, assemblies);

    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans the provided assemblies for handlers.
    /// </summary>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="assemblies">Assemblies to scan for handlers.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate<TNotifier>(
        this IServiceCollection services,
        params Assembly[] assemblies
    ) where TNotifier : class, INotifiable
        => NetMediate<TNotifier>(services, assemblies);

    /// <summary>
    /// Configures and registers mediator services for the specified notifier type, mapping handlers from the provided
    /// assemblies.
    /// </summary>
    /// <remarks>Use this method to set up mediator-based notification and request handling by specifying the
    /// notifier type and the assemblies containing handler implementations. This method is typically called during
    /// application startup as part of dependency injection configuration.</remarks>
    /// <typeparam name="TNotifier">The type of notifier to be used with the mediator. Must implement the INotifiable interface.</typeparam>
    /// <param name="services">The service collection to which mediator services will be added. Cannot be null.</param>
    /// <param name="assemblies">An array of assemblies to scan for handler implementations. Only types in these assemblies will be considered
    /// for registration.</param>
    /// <returns>An IMediatorServiceBuilder instance for further configuration of mediator services.</returns>
    private static IMediatorServiceBuilder NetMediate<TNotifier>(
        IServiceCollection services,
        params Assembly[] assemblies
    ) where TNotifier : class, INotifiable
        => new MediatorServiceBuilder<TNotifier>(services).MapAssemblies(assemblies);
}
