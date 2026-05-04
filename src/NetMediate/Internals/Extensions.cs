using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal static class Extensions
{
    // Handlers are registered as Singletons, so their resolved arrays never change across the
    // lifetime of the application.  A single global cache keyed by service type is correct and
    // avoids any per-container overhead.
    private static readonly ConcurrentDictionary<Type, Lazy<object>> s_handlerCache = new();

    // Behaviors may differ between service-provider instances (e.g., different test containers
    // register different behaviors for the same message type).  Using a ConditionalWeakTable
    // keys the per-type behavior cache to the concrete IServiceProvider instance, so each
    // container gets its own isolated cache.  When the provider is GC'd its cache entry is
    // automatically released — no memory leak.
    private static readonly ConditionalWeakTable<IServiceProvider, ConcurrentDictionary<Type, Lazy<object>>>
        s_behaviorCacheByProvider = new();

    /// <summary>
    /// Clears the static handler cache.  Optionally clears the behavior cache for a specific
    /// provider instance.  Intended for test isolation only — in production, prefer using
    /// distinct message types per handler registration to avoid cache contamination.
    /// </summary>
    internal static void ClearCache(IServiceProvider? serviceProvider = null)
    {
        s_handlerCache.Clear();

        if (serviceProvider is not null)
            s_behaviorCacheByProvider.Remove(serviceProvider);
        // If no provider is supplied, we do NOT attempt to clear all per-provider entries —
        // ConditionalWeakTable does not expose a Clear() on all target frameworks and the
        // per-provider caches are naturally empty for any freshly created ServiceProvider.
    }

    extension(IServiceProvider serviceProvider)
    {
        public THandler[] GetHandlers<THandler, TMessage, TResult>()
            where THandler : class, IHandler<TMessage, TResult>
            where TMessage : notnull
            where TResult : notnull
        {
            return serviceProvider.GetCachedServices<THandler>(s_handlerCache);
        }

        public T[] GetCachedBehaviors<T>() where T : class
        {
            // Retrieve (or lazily create) the per-provider behavior dictionary, then cache
            // within it — identical pattern to GetHandlers but scoped to this provider.
            var providerCache = s_behaviorCacheByProvider.GetOrCreateValue(serviceProvider);
            return serviceProvider.GetCachedServices<T>(providerCache);
        }

        private T[] GetCachedServices<T>(ConcurrentDictionary<Type, Lazy<object>> cache) where T : class
        {
            var lazy = cache.GetOrAdd(
                typeof(T),
                _ => new Lazy<object>(
                    () => serviceProvider.GetServices<T>().ToArray(),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            return (T[])lazy.Value;
        }
    }
}
