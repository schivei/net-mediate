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
    /// <remarks>
    /// This overload uses reflection-based assembly scanning and is not compatible with NativeAOT or trimming.
    /// For AOT-compatible registration, use <c>AddNetMediateGenerated()</c> from the <c>NetMediate.SourceGeneration</c>
    /// package, or the <see cref="AddNetMediate(IServiceCollection, Action{IMediatorServiceBuilder})"/> overload
    /// that accepts an explicit configuration action.
    /// </remarks>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection to discover handler types. " +
        "Use AddNetMediate(IServiceCollection, Action<IMediatorServiceBuilder>) with explicit handler registration " +
        "or NetMediate.SourceGeneration for a trim-safe path."
    )]
    public static IMediatorServiceBuilder AddNetMediate(this IServiceCollection services) =>
        NetMediate<Notifier>(
            services,
            []
        );

    /// <summary>
    /// Adds NetMediate services to the specified service collection using the provided notifier type.
    /// </summary>
    /// <remarks>
    /// This overload uses reflection-based assembly scanning and is not compatible with NativeAOT or trimming.
    /// </remarks>
    /// <typeparam name="TNotifier">The type that implements the notification interface to be used for notifications.</typeparam>
    /// <param name="services">The service collection to which NetMediate services will be added.</param>
    /// <returns>An IMediatorServiceBuilder that can be used to further configure NetMediate services.</returns>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection to discover handler types. " +
        "Use AddNetMediate(IServiceCollection, Action<IMediatorServiceBuilder>) with explicit handler registration " +
        "or NetMediate.SourceGeneration for a trim-safe path."
    )]
    public static IMediatorServiceBuilder AddNetMediate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNotifier>(this IServiceCollection services)
        where TNotifier : class, INotifiable =>
        NetMediate<TNotifier>(
            services,
            []
        );

    /// <summary>
    /// Adds NetMediate core services without assembly scanning and applies custom registration configuration.
    /// This overload is useful for source-generated handler registration to avoid reflection at startup.
    /// </summary>
    /// <remarks>
    /// This overload does <em>not</em> scan assemblies for handlers and is compatible with NativeAOT and trimming
    /// when all handler types are explicitly registered inside <paramref name="configure"/>.
    /// </remarks>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="configure">The callback that performs explicit handler registrations.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    public static IMediatorServiceBuilder AddNetMediate(
        this IServiceCollection services,
        Action<IMediatorServiceBuilder> configure
    )
    {
        Guard.ThrowIfNull(configure);

        // Direct instantiation (without MapAssemblies) is intentional: this overload is the
        // AOT/trim-safe path. Assembly scanning is deliberately omitted so that the caller's
        // configure callback is the sole source of handler registrations.
        var builder = new MediatorServiceBuilder<Notifier>(services);
        configure(builder);
        return builder;
    }

    /// <summary>
    /// Adds NetMediate core services without assembly scanning, using the provided notifier type, and applies
    /// custom registration configuration.
    /// </summary>
    /// <remarks>
    /// This overload does <em>not</em> scan assemblies for handlers and is compatible with NativeAOT and trimming
    /// when all handler types are explicitly registered inside <paramref name="configure"/>.
    /// </remarks>
    /// <typeparam name="TNotifier">The type that implements the notification interface and will be used for notification handling.</typeparam>
    /// <param name="services">The service collection to which NetMediate services will be added.</param>
    /// <param name="configure">A delegate that configures the mediator service builder after the core services have been registered. Cannot be
    /// null.</param>
    /// <returns>An instance of IMediatorServiceBuilder that can be used to further configure mediator services.</returns>
    public static IMediatorServiceBuilder AddNetMediate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNotifier>(
        this IServiceCollection services,
        Action<IMediatorServiceBuilder> configure
    ) where TNotifier : class, INotifiable
    {
        Guard.ThrowIfNull(configure);

        // Direct instantiation (without MapAssemblies) is intentional: this overload is the
        // AOT/trim-safe path. Assembly scanning is deliberately omitted so that the caller's
        // configure callback is the sole source of handler registrations.
        var builder = new MediatorServiceBuilder<TNotifier>(services);
        configure(builder);
        return builder;
    }

    /// <summary>
    /// Adds NetMediate mediator services to the specified service collection and registers handlers from the provided
    /// assemblies.
    /// </summary>
    /// <remarks>
    /// This overload uses reflection-based assembly scanning and is not compatible with NativeAOT or trimming.
    /// </remarks>
    /// <param name="services">The service collection to which the mediator services will be added. Cannot be null.</param>
    /// <param name="assemblies">An array of assemblies to scan for handler implementations. At least one assembly must be provided.</param>
    /// <returns>An IMediatorServiceBuilder that can be used to further configure mediator services.</returns>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection to discover handler types. " +
        "Use AddNetMediate(IServiceCollection, Action<IMediatorServiceBuilder>) with explicit handler registration " +
        "or NetMediate.SourceGeneration for a trim-safe path."
    )]
    public static IMediatorServiceBuilder AddNetMediate(
        this IServiceCollection services,
        params Assembly[] assemblies
    ) => NetMediate<Notifier>(services, assemblies);

    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans the provided assemblies for handlers.
    /// </summary>
    /// <remarks>
    /// This overload uses reflection-based assembly scanning and is not compatible with NativeAOT or trimming.
    /// </remarks>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="assemblies">Assemblies to scan for handlers.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection to discover handler types. " +
        "Use AddNetMediate(IServiceCollection, Action<IMediatorServiceBuilder>) with explicit handler registration " +
        "or NetMediate.SourceGeneration for a trim-safe path."
    )]
    public static IMediatorServiceBuilder AddNetMediate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNotifier>(
        this IServiceCollection services,
        params Assembly[] assemblies
    ) where TNotifier : class, INotifiable
        => NetMediate<TNotifier>(services, assemblies);

    /// <summary>
    /// Configures and registers mediator services for the specified notifier type, mapping handlers from the provided
    /// assemblies.
    /// </summary>
    /// <typeparam name="TNotifier">The type of notifier to be used with the mediator. Must implement the INotifiable interface.</typeparam>
    /// <param name="services">The service collection to which mediator services will be added. Cannot be null.</param>
    /// <param name="assemblies">An array of assemblies to scan for handler implementations. Only types in these assemblies will be considered
    /// for registration.</param>
    /// <returns>An IMediatorServiceBuilder instance for further configuration of mediator services.</returns>
    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection to discover handler types."
    )]
    private static IMediatorServiceBuilder NetMediate<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNotifier>(
        IServiceCollection services,
        params Assembly[] assemblies
    ) where TNotifier : class, INotifiable
        => new MediatorServiceBuilder<TNotifier>(services).MapAssemblies(assemblies);
}
