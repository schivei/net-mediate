using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal static class Extensions
{
    // Handlers are registered as singletons, so their resolved arrays never change at runtime.
    // A permanent Lazy<T> cache avoids repeated DI resolution without any expiry overhead.
    private static readonly ConcurrentDictionary<Type, Lazy<object>> s_handlerCache = new();

    /// <summary>
    /// Clears the static handler cache. Intended for test isolation only.
    /// In production, prefer using distinct message types per handler registration
    /// to avoid cache contamination.
    /// </summary>
    internal static void ClearCache()
    {
        s_handlerCache.Clear();
    }

    extension(IServiceProvider serviceProvider)
    {
        public THandler[] GetHandlers<THandler, TMessage, TResult>() where THandler : IHandler<TMessage, TResult> where TMessage : notnull where TResult : notnull
        {
            return serviceProvider.GetCachedServices<THandler>();
        }

        private T[] GetCachedServices<T>()
        {
            var lazy = s_handlerCache.GetOrAdd(
                typeof(T),
                _ => new Lazy<object>(
                    () => serviceProvider.GetServices<T>().ToArray(),
                    LazyThreadSafetyMode.ExecutionAndPublication));

            return (T[])lazy.Value;
        }
    }
}
