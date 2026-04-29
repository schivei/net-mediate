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
    /// This overload uses runtime reflection to discover all loaded assemblies.
    /// It is not compatible with Native AOT or aggressive linker trimming.
    /// Use <see cref="AddNetMediate(IServiceCollection, Action{IMediatorServiceBuilder})"/> or
    /// <see cref="AddNetMediate{T}(IServiceCollection)"/> together with the
    /// <c>NetMediate.SourceGeneration</c> analyzer for AOT-safe registration.
    /// </remarks>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
#if NET5_0_OR_GREATER
    [RequiresUnreferencedCode(
        "This method scans all currently loaded assemblies for handler types using reflection " +
        "and is not compatible with trimming or Native AOT. " +
        "Use AddNetMediate(IServiceCollection, Action<IMediatorServiceBuilder>) with explicit " +
        "handler registrations or the NetMediate.SourceGeneration source generator instead."
    )]
#endif
    public static IMediatorServiceBuilder AddNetMediate(this IServiceCollection services)
    {
        var assemblies = AppDomain
            .CurrentDomain.GetAssemblies()
            .Where(static a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .ToArray();
        return AddNetMediate(services, assemblies);
    }

    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans the assembly containing the specified type <typeparamref name="T"/> for handlers.
    /// </summary>
    /// <remarks>
    /// This overload uses reflection to enumerate the types in the assembly that contains
    /// <typeparamref name="T"/> and is not compatible with Native AOT or aggressive linker trimming.
    /// Use <see cref="AddNetMediate(IServiceCollection, Action{IMediatorServiceBuilder})"/> together
    /// with the <c>NetMediate.SourceGeneration</c> source generator for AOT-safe registration.
    /// </remarks>
    /// <typeparam name="T">A type from the assembly to scan for handlers.</typeparam>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
#if NET5_0_OR_GREATER
    [RequiresUnreferencedCode(
        "This method scans an assembly for handler types using reflection " +
        "and is not compatible with trimming or Native AOT. " +
        "Use AddNetMediate(IServiceCollection, Action<IMediatorServiceBuilder>) with explicit " +
        "handler registrations or the NetMediate.SourceGeneration source generator instead."
    )]
#endif
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

        var builder = AddNetMediate(services, Array.Empty<Assembly>());
        configure(builder);
        return builder;
    }

    /// <summary>
    /// Adds NetMediate services to the specified <see cref="IServiceCollection"/> and returns a <see cref="IMediatorServiceBuilder"/>
    /// for further configuration. This method scans the provided assemblies for handlers.
    /// </summary>
    /// <remarks>
    /// This overload uses reflection to enumerate handler types from the supplied assemblies
    /// and is not compatible with Native AOT or aggressive linker trimming.
    /// Use <see cref="AddNetMediate(IServiceCollection, Action{IMediatorServiceBuilder})"/> together
    /// with the <c>NetMediate.SourceGeneration</c> source generator for AOT-safe registration.
    /// </remarks>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="assemblies">Assemblies to scan for handlers.</param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
#if NET5_0_OR_GREATER
    [RequiresUnreferencedCode(
        "This method scans assemblies for handler types using reflection " +
        "and is not compatible with trimming or Native AOT. " +
        "Use AddNetMediate(IServiceCollection, Action<IMediatorServiceBuilder>) with explicit " +
        "handler registrations or the NetMediate.SourceGeneration source generator instead."
    )]
#endif
    public static IMediatorServiceBuilder AddNetMediate(
        this IServiceCollection services,
        params Assembly[] assemblies
    ) => new MediatorServiceBuilder(services).MapAssemblies(assemblies);
}
