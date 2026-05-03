using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;

namespace NetMediate.Internals;

internal static class Extensions
{
    private static readonly ConcurrentDictionary<Type, long> s_serviceUsage = [];
    private static readonly ConditionalWeakTable<IServiceProvider, IMemoryCache> s_caches = new();

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

    extension(IServiceProvider serviceProvider)
    {
        public THandler[] GetHandlers<THandler, TMessage, TResult>() where THandler : IHandler<TMessage, TResult> where TMessage : notnull where TResult : notnull
        {
            return serviceProvider.GetCachedServices<THandler>();
        }

        private IMemoryCache GetCache()
        {
            return s_caches.GetValue(serviceProvider, static sp =>
                sp.GetService<IMemoryCache>() ?? new MemoryCache(new MemoryCacheOptions()));
        }

        private T[] GetCachedServices<T>()
        {
            var serviceType = typeof(T);
            
            SetUsage(serviceType);

            return serviceProvider.GetCache().GetOrCreate(serviceType, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CalculateCachePersistence(serviceType);
                return serviceProvider.GetServices<T>().ToArray();
            });
        }
    }
}