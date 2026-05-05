using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

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

    extension(IMediatorServiceBuilder mediatorServiceBuilder)
    {
        internal void RegisterCommandHandler<TMessage>(ICommandHandler<TMessage> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddSingleton(handler);
            mediatorServiceBuilder.Services.TryAddSingleton<PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>>();
        }

        internal void RegisterNotificationHandler<TMessage>(INotificationHandler<TMessage> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddSingleton(handler);
            mediatorServiceBuilder.Services.TryAddSingleton<NotificationPipelineExecutor<TMessage>>();
        }

        internal void RegisterRequestHandler<TMessage, TResponse>(IRequestHandler<TMessage, TResponse> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddSingleton(handler);
            mediatorServiceBuilder.Services.TryAddSingleton<RequestPipelineExecutor<TMessage, TResponse>>();
        }

        internal void RegisterStreamHandler<TMessage, TResponse>(IStreamHandler<TMessage, TResponse> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddSingleton(handler);
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
            return serviceProvider.GetCachedServices<THandler>(new(typeof(THandler), key), s_handlerCache).Single();
        }

        public THandler[] GetHandlers<THandler, TMessage, TResult>(object? key = null)
            where THandler : class, IHandler<TMessage, TResult>
            where TMessage : notnull
            where TResult : notnull
        {
            return serviceProvider.GetCachedServices<THandler>(new(typeof(THandler), key), s_handlerCache);
        }

        public T[] GetCachedBehaviors<T>() where T : class
        {
            var providerCache = s_behaviorCacheByProvider.GetOrCreateValue(serviceProvider);
            return serviceProvider.GetCachedServices<T>(new(typeof(T), null), providerCache);
        }

        private T[] GetCachedServices<T>(ServiceKey key, ConcurrentDictionary<ServiceKey, Lazy<object>> cache) where T : class
        {
            var lazy = cache.GetOrAdd(
                key,
                sk => new Lazy<object>(
                    () => serviceProvider.GetKeyedServices<T>(sk).ToArray(),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            );

            return (T[])lazy.Value;
        }
    }
}
