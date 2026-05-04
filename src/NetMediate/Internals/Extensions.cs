using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal static class Extensions
{
    // Handlers are registered as Singletons, so their resolved arrays never change across the
    // lifetime of the application.  A single global cache keyed by service type is correct and
    // avoids any per-container overhead.
    private static readonly ConcurrentDictionary<ServiceKey, Lazy<object>> s_handlerCache = new();

    // Behaviors may differ between service-provider instances (e.g., different test containers
    // register different behaviors for the same message type).  Using a ConditionalWeakTable
    // keys the per-type behavior cache to the concrete IServiceProvider instance, so each
    // container gets its own isolated cache.  When the provider is GC'd its cache entry is
    // automatically released — no memory leak.
    private static readonly ConditionalWeakTable<IServiceProvider, ConcurrentDictionary<ServiceKey, Lazy<object>>>
        s_behaviorCacheByProvider = new();

    // Pre-compiled pipeline delegates are cached per provider per executor type.
    // Each concrete executor class (closed generic) registers an Action<IServiceProvider> here
    // via RegisterPipelineCacheClearing(); ClearCache(provider) invokes all of them so the
    // pre-compiled chains are rebuilt on the next call (used by test isolation helpers).
    private static readonly List<Action<IServiceProvider>> s_pipelineCacheClearers = [];

    /// <summary>
    /// Called once per closed-generic executor type (from a static constructor) to register
    /// the cache-clearing action for that type's pre-compiled pipeline cache.
    /// </summary>
    internal static void RegisterPipelineCacheClearing(Action<IServiceProvider> clearer)
    {
        lock (s_pipelineCacheClearers)
            s_pipelineCacheClearers.Add(clearer);
    }

    /// <summary>
    /// Clears the static handler cache.  Optionally clears the behavior cache and pre-compiled
    /// pipeline caches for a specific provider instance.  Intended for test isolation only —
    /// in production, prefer using distinct message types per handler registration to avoid
    /// cache contamination.
    /// </summary>
    internal static void ClearCache(IServiceProvider? serviceProvider = null)
    {
        s_handlerCache.Clear();

        if (serviceProvider is not null)
        {
            s_behaviorCacheByProvider.Remove(serviceProvider);

            lock (s_pipelineCacheClearers)
            {
                foreach (var clear in s_pipelineCacheClearers)
                    clear(serviceProvider);
            }
        }
    }

    extension(IServiceProvider serviceProvider)
    {
        public THandler[] GetHandlers<THandler, TMessage, TResult>(object? key = null)
            where THandler : class, IHandler<TMessage, TResult>
            where TMessage : notnull
            where TResult : notnull
        {
            return serviceProvider.GetCachedServices<THandler>(new(typeof(THandler), key), s_handlerCache);
        }

        public T[] GetCachedBehaviors<T>(object? key = null) where T : class
        {
            var providerCache = s_behaviorCacheByProvider.GetOrCreateValue(serviceProvider);
            return serviceProvider.GetCachedServices<T>(new(typeof(T), key), providerCache);
        }

        private T[] GetCachedServices<T>(ServiceKey key, ConcurrentDictionary<ServiceKey, Lazy<object>> cache) where T : class
        {
            var lazy = cache.GetOrAdd(
                key,
                _ => new Lazy<object>(
                    () => serviceProvider.GetKeyedServices<T>(key).ToArray(),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            return (T[])lazy.Value;
        }
    }
}
