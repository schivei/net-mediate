using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;

namespace NetMediate;

/// <summary>
/// Provides the internal extension method used by source-generated code to register NetMediate
/// services with the dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// <b>Do not call <see cref="UseNetMediate(IServiceCollection,Action{IMediatorServiceBuilder})"/>
/// directly.</b>  Use the <c>AddNetMediate()</c> extension method emitted by the
/// <c>NetMediate.SourceGeneration</c> source generator instead.  That method is generated at
/// compile time inside your project and calls this method with all discovered handlers
/// registered in an AOT-safe, trim-safe manner.
/// </para>
/// <para>
/// Calling <c>AddNetMediate()</c> more than once on the same <see cref="IServiceCollection"/> is
/// safe: subsequent calls are silently ignored (a debug-level warning is written to
/// <see cref="Debug"/>) and the original registration is preserved.
/// </para>
/// </remarks>
[ExcludeFromCodeCoverage]
public static class NetMediateDI
{
    // Tracks which IServiceCollection instances have already had UseNetMediate called.
    // ConditionalWeakTable keys on the live collection reference, so each independent DI
    // container gets its own isolation without any cross-container leakage (safe for tests
    // that build multiple containers in the same process).  When a collection is GC-eligible
    // the entry is automatically released — no memory leak.
    private static readonly ConditionalWeakTable<IServiceCollection, object> s_started = new ConditionalWeakTable<IServiceCollection, object>();

    // Plain object lock (not System.Threading.Lock) so no polyfill dependency in this file.
    private static readonly object s_startedLock = new();

    /// <summary>
    /// Configures NetMediate core services and applies the provided explicit handler registration
    /// callback.  This overload is <em>intended to be called by source-generated code only</em>
    /// — prefer the generated <c>AddNetMediate()</c> extension method over calling this method
    /// directly.
    /// </summary>
    /// <remarks>
    /// This overload is AOT- and trim-safe when all handler types are explicitly registered
    /// inside <paramref name="configure"/>.  It does <b>not</b> scan assemblies for handlers.
    /// Subsequent calls on the same <paramref name="services"/> are silently ignored.
    /// </remarks>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="configure">
    /// The callback that performs explicit handler and behavior registrations.
    /// </param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IMediatorServiceBuilder UseNetMediate(
        this IServiceCollection services,
        Action<IMediatorServiceBuilder> configure
    )
    {
        Guard.ThrowIfNull(configure);

        // Idempotency guard: if UseNetMediate has already been called on this container, log a
        // warning and return a no-op builder so callers can still chain off the return value.
        lock (s_startedLock)
        {
            if (s_started.TryGetValue(services, out _))
            {
                Debug.WriteLine(
                    "[NetMediate] UseNetMediate was called more than once on the same IServiceCollection. " +
                    "The duplicate call is ignored. Ensure AddNetMediate() is called only once " +
                    "at application startup."
                );
                return new MediatorServiceBuilder<Notifier>(services, skipCoreRegistration: true);
            }

            s_started.Add(services, new object());
        }

        var builder = new MediatorServiceBuilder<Notifier>(services);
        configure(builder);
        return builder;
    }

    /// <summary>
    /// Configures NetMediate core services with a custom notifier type and applies the provided
    /// explicit handler registration callback.  This overload is <em>intended to be called by
    /// source-generated code only</em>.
    /// </summary>
    /// <typeparam name="TNotifier">
    /// The type implementing <see cref="INotifiable"/> to use for notification handling.
    /// </typeparam>
    /// <param name="services">The service collection to add NetMediate services to.</param>
    /// <param name="configure">
    /// The callback that performs explicit handler and behavior registrations.
    /// </param>
    /// <returns>A <see cref="IMediatorServiceBuilder"/> instance for additional configuration.</returns>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IMediatorServiceBuilder UseNetMediate<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TNotifier>(
        this IServiceCollection services,
        Action<IMediatorServiceBuilder> configure
    ) where TNotifier : class, INotifiable
    {
        Guard.ThrowIfNull(configure);

        lock (s_startedLock)
        {
            if (s_started.TryGetValue(services, out _))
            {
                Debug.WriteLine(
                    "[NetMediate] UseNetMediate was called more than once on the same IServiceCollection. " +
                    "The duplicate call is ignored."
                );
                return new MediatorServiceBuilder<TNotifier>(services, skipCoreRegistration: true);
            }

            s_started.Add(services, new object());
        }

        var builder = new MediatorServiceBuilder<TNotifier>(services);
        configure(builder);
        return builder;
    }
}
