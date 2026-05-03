using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;

namespace NetMediate.Internals;

internal static class Extensions
{
    private static readonly ConcurrentDictionary<Type, long> s_serviceUsage = [];
    private static readonly object s_cacheLock = new();
    private static IMemoryCache? _sCache;

    private static void SetUsage(Type serviceType)
    {
        s_serviceUsage.AddOrUpdate(serviceType, 1, (_, count) => count + 1);
    }

    private static TimeSpan CalculateCachePersistence(Type serviceType)
    {
        var timeLimit = TimeSpan.FromMinutes(5);
        if (s_serviceUsage.TryGetValue(serviceType, out var count))
        {
            timeLimit += TimeSpan.FromTicks(count * TimeSpan.TicksPerMinute);
        }

        if (timeLimit <= TimeSpan.Zero)
            timeLimit = TimeSpan.FromMinutes(5);

        if (timeLimit > TimeSpan.FromHours(1))
            timeLimit = TimeSpan.FromHours(1);

        return timeLimit;
    }

    /// <summary>
    /// Clears the static handler cache. Intended for test isolation only.
    /// In production, prefer using distinct message types per handler registration
    /// to avoid cache contamination.
    /// </summary>
    internal static void ClearCache()
    {
        lock (s_cacheLock)
        {
            _sCache?.Dispose();
            _sCache = null;
        }

        s_serviceUsage.Clear();
    }

    extension(IServiceProvider serviceProvider)
    {
        public THandler[] GetHandlers<THandler, TMessage, TResult>() where THandler : IHandler<TMessage, TResult> where TMessage : notnull where TResult : notnull
        {
            return serviceProvider.GetCachedServices<THandler>();
        }

        private IMemoryCache GetCache()
        {
            if (_sCache is not null) return _sCache;
            lock (s_cacheLock)
            {
                return _sCache ??= serviceProvider.GetService<IMemoryCache>() ??
                                   new MemoryCache(new MemoryCacheOptions());
            }
        }

        private T[] GetCachedServices<T>()
        {
            var serviceType = typeof(T);

            SetUsage(serviceType);

            return serviceProvider.GetCache().GetOrCreate(serviceType, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CalculateCachePersistence(serviceType);
                return serviceProvider.GetServices<T>().ToArray();
            }) ?? [];
        }
    }
}
