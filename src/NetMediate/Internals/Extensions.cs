using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NetMediate.Internals;

internal static class Extensions
{
    public const string DEFAULT_ROUTING_KEY = "__default";

    // Handlers are registered as Singletons, but resolving them via a global static cache would
    // contaminate distinct IServiceProvider instances (e.g., separate test containers) that
    // register different handlers for the same message/key combination.  Using a
    // ConditionalWeakTable keys the per-type handler cache to the concrete IServiceProvider
    // instance so each container gets its own isolated cache.  When the provider is GC'd its
    // cache entry is automatically released — no memory leak.
    private static readonly ConditionalWeakTable<IServiceProvider, ConcurrentDictionary<ServiceKey, Lazy<object>>>
        s_handlerCacheByProvider = new();

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
    /// Clears the handler cache, behavior cache, and pre-compiled pipeline caches for a
    /// specific provider instance.  Intended for test isolation only — in production, prefer
    /// using distinct message types per handler registration to avoid cache contamination.
    /// </summary>
    internal static void ClearCache(IServiceProvider? serviceProvider = null)
    {
        if (serviceProvider is not null)
        {
            s_handlerCacheByProvider.Remove(serviceProvider);
            s_behaviorCacheByProvider.Remove(serviceProvider);

            lock (s_pipelineCacheClearers)
            {
                foreach (var clear in s_pipelineCacheClearers)
                    clear(serviceProvider);
            }
        }
    }

    extension(IMediatorServiceBuilder mediatorServiceBuilder)
    {
        internal void RegisterCommandHandler<TMessage>(ICommandHandler<TMessage> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddKeyedSingleton(DEFAULT_ROUTING_KEY, handler);
            mediatorServiceBuilder.Services.TryAddSingleton<PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>>();
        }

        internal void RegisterNotificationHandler<TMessage>(INotificationHandler<TMessage> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddKeyedSingleton(DEFAULT_ROUTING_KEY, handler);
            mediatorServiceBuilder.Services.TryAddSingleton<NotificationPipelineExecutor<TMessage>>();
        }

        internal void RegisterRequestHandler<TMessage, TResponse>(IRequestHandler<TMessage, TResponse> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddKeyedSingleton(DEFAULT_ROUTING_KEY, handler);
            mediatorServiceBuilder.Services.TryAddSingleton<RequestPipelineExecutor<TMessage, TResponse>>();
        }

        internal void RegisterStreamHandler<TMessage, TResponse>(IStreamHandler<TMessage, TResponse> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddKeyedSingleton(DEFAULT_ROUTING_KEY, handler);
            mediatorServiceBuilder.Services.TryAddSingleton<StreamPipelineExecutor<TMessage, TResponse>>();
        }
    }

    extension(IServiceProvider serviceProvider)
    {
        public THandler GetHandler<THandler, TMessage, TResult>(object? key = null)
            where THandler : class, IHandler<TMessage, TResult>
            where TMessage : notnull
            where TResult : notnull
        {
            var providerCache = s_handlerCacheByProvider.GetValue(serviceProvider, _ => new());
            return serviceProvider.GetCachedServices<THandler>(new(typeof(THandler), key ?? DEFAULT_ROUTING_KEY), providerCache).Single();
        }

        public THandler[] GetHandlers<THandler, TMessage, TResult>(object? key = null)
            where THandler : class, IHandler<TMessage, TResult>
            where TMessage : notnull
            where TResult : notnull
        {
            var providerCache = s_handlerCacheByProvider.GetValue(serviceProvider, _ => new());
            return serviceProvider.GetCachedServices<THandler>(new(typeof(THandler), key ?? DEFAULT_ROUTING_KEY), providerCache);
        }

        public T[] GetCachedBehaviors<T>() where T : class
        {
            var providerCache = s_behaviorCacheByProvider.GetOrCreateValue(serviceProvider);
            var cacheKey = new ServiceKey(typeof(T), DEFAULT_ROUTING_KEY);
            var lazy = providerCache.GetOrAdd(
                cacheKey,
                _ => new Lazy<object>(
                    () =>
                    {
                        // Combine behaviors registered under the default routing key (via
                        // RegisterBehavior) with any behaviors registered without a key (plain
                        // AddScoped/AddSingleton/AddTransient).  The keyed set comes first so
                        // the registration-order declared through IMediatorServiceBuilder is
                        // preserved; unkeyed registrations are appended for backward compat.
                        var keyed = serviceProvider.GetKeyedServices<T>(DEFAULT_ROUTING_KEY).ToArray();
                        var unkeyed = serviceProvider.GetServices<T>().ToArray();
                        if (keyed.Length == 0) return unkeyed;
                        if (unkeyed.Length == 0) return keyed;
                        return keyed.Concat(unkeyed).ToArray();
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            );
            return (T[])lazy.Value;
        }

        private T[] GetCachedServices<T>(ServiceKey key, ConcurrentDictionary<ServiceKey, Lazy<object>> cache) where T : class
        {
            var lazy = cache.GetOrAdd(
                key,
                sk => new Lazy<object>(
                    () => serviceProvider.GetKeyedServices<T>(sk.Key ?? DEFAULT_ROUTING_KEY).ToArray(),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            );

            return (T[])lazy.Value;
        }
    }
}
