using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.Internals;

internal static class Extensions
{
    public const string DEFAULT_ROUTING_KEY = "__default";

    private static readonly ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<ServiceKey, Lazy<object>>
    > s_handlerCacheByProvider = new();

    private static readonly ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<ServiceKey, Lazy<object>>
    > s_behaviorCacheByProvider = new();

    private static readonly List<Action<IServiceProvider>> s_pipelineCacheClears = [];

    /// <summary>
    /// Called once per closed-generic executor type (from a static constructor) to register
    /// the cache-clearing action for that type's pre-compiled pipeline cache.
    /// </summary>
    internal static void RegisterPipelineCacheClearing(Action<IServiceProvider> clearer)
    {
        lock (s_pipelineCacheClears)
            s_pipelineCacheClears.Add(clearer);
    }
    
    /// <summary>
    /// Clears the handler cache, behavior cache, and pre-compiled pipeline caches for a
    /// specific provider instance.  Intended for test isolation only — in production, prefer
    /// using distinct message types per handler registration to avoid cache contamination.
    /// </summary>
    internal static void ClearCache(IServiceProvider? serviceProvider = null)
    {
        if (serviceProvider is null)
            return;
        
        s_handlerCacheByProvider.Remove(serviceProvider);
        s_behaviorCacheByProvider.Remove(serviceProvider);

        lock (s_pipelineCacheClears)
        {
            foreach (var clear in s_pipelineCacheClears)
                clear(serviceProvider);
        }
    }

    extension(IMediatorServiceBuilder mediatorServiceBuilder)
    {
        internal void RegisterCommandHandler<TMessage>(ICommandHandler<TMessage> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddKeyedSingleton(DEFAULT_ROUTING_KEY, handler);
            mediatorServiceBuilder.Services.TryAddSingleton<
                PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>
            >();
        }

        internal void RegisterNotificationHandler<TMessage>(INotificationHandler<TMessage> handler)
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddKeyedSingleton(DEFAULT_ROUTING_KEY, handler);
            mediatorServiceBuilder.Services.TryAddSingleton<
                NotificationPipelineExecutor<TMessage>
            >();
        }

        internal void RegisterRequestHandler<TMessage, TResponse>(
            IRequestHandler<TMessage, TResponse> handler
        )
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddKeyedSingleton(DEFAULT_ROUTING_KEY, handler);
            mediatorServiceBuilder.Services.TryAddSingleton<
                RequestPipelineExecutor<TMessage, TResponse>
            >();
        }

        internal void RegisterStreamHandler<TMessage, TResponse>(
            IStreamHandler<TMessage, TResponse> handler
        )
            where TMessage : notnull
        {
            mediatorServiceBuilder.Services.AddKeyedSingleton(DEFAULT_ROUTING_KEY, handler);
            mediatorServiceBuilder.Services.TryAddSingleton<
                StreamPipelineExecutor<TMessage, TResponse>
            >();
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
            return serviceProvider
                .GetCachedServices<THandler>(
                    new(typeof(THandler), key ?? DEFAULT_ROUTING_KEY),
                    providerCache
                )
                .Single();
        }

        public THandler[] GetHandlers<THandler, TMessage, TResult>(object? key = null)
            where THandler : class, IHandler<TMessage, TResult>
            where TMessage : notnull
            where TResult : notnull
        {
            var providerCache = s_handlerCacheByProvider.GetValue(serviceProvider, _ => new());
            return serviceProvider.GetCachedServices<THandler>(
                new(typeof(THandler), key ?? DEFAULT_ROUTING_KEY),
                providerCache
            );
        }

        public T[] GetCachedBehaviors<T>()
            where T : class
        {
            var providerCache = s_behaviorCacheByProvider.GetOrCreateValue(serviceProvider);
            var cacheKey = new ServiceKey(typeof(T), DEFAULT_ROUTING_KEY);
            var lazy = providerCache.GetOrAdd(
                cacheKey,
                _ => new Lazy<object>(
                    () =>
                    {
                        var keyed = serviceProvider
                            .GetKeyedServices<T>(DEFAULT_ROUTING_KEY)
                            .ToArray();
                        var unkeyed = serviceProvider.GetServices<T>().ToArray();
                        if (keyed.Length == 0)
                            return unkeyed;
                        if (unkeyed.Length == 0)
                            return keyed;
                        var combined = new T[keyed.Length + unkeyed.Length];
                        keyed.CopyTo(combined, 0);
                        unkeyed.CopyTo(combined, keyed.Length);
                        return combined;
                    },
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            );
            return (T[])lazy.Value;
        }

        private T[] GetCachedServices<T>(
            ServiceKey key,
            ConcurrentDictionary<ServiceKey, Lazy<object>> cache
        )
            where T : class
        {
            var lazy = cache.GetOrAdd(
                key,
                sk => new Lazy<object>(
                    () =>
                        serviceProvider
                            .GetKeyedServices<T>(sk.Key ?? DEFAULT_ROUTING_KEY)
                            .ToArray(),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            );

            return (T[])lazy.Value;
        }
    }
}
