using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal static class Extensions
{
    // Handlers are registered as singletons, so their resolved arrays never change at runtime.
    // A permanent Lazy<T> cache avoids repeated DI resolution without any expiry overhead.
    private static readonly ConcurrentDictionary<Type, Lazy<object>> s_handlerCache = new();

    // Behaviors are registered as transient but are stateless cross-cutting concerns.
    // Caching the resolved behavior arrays per message-result type avoids repeated DI enumeration,
    // making pipeline setup O(1) after the first dispatch of a given message type.
    private static readonly ConcurrentDictionary<Type, Lazy<object>> s_behaviorCache = new();

    /// <summary>
    /// Clears the static handler and behavior caches. Intended for test isolation only.
    /// In production, prefer using distinct message types per handler registration
    /// to avoid cache contamination.
    /// </summary>
    internal static void ClearCache()
    {
        s_handlerCache.Clear();
        s_behaviorCache.Clear();
    }

    extension(IServiceProvider serviceProvider)
    {
        public THandler[] GetHandlers<THandler, TMessage, TResult>() where THandler : IHandler<TMessage, TResult> where TMessage : notnull where TResult : notnull
        {
            return serviceProvider.GetCachedServices<THandler>(s_handlerCache);
        }

        public T[] GetCachedBehaviors<T>() where T : class
        {
            return serviceProvider.GetCachedServices<T>(s_behaviorCache);
        }

        private T[] GetCachedServices<T>(ConcurrentDictionary<Type, Lazy<object>> cache)
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
