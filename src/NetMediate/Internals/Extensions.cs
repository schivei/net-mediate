using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Caching.Memory;

namespace NetMediate.Internals;

internal static class Extensions
{
    private static readonly ConcurrentDictionary<Type, long> s_serviceUsage = [];

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
    /// Per-service-provider handler cache. Used when the container provides <see cref="IMemoryCache"/>
    /// (via <c>services.AddMemoryCache()</c>). When no <see cref="IMemoryCache"/> is available the
    /// lookup always falls through to <see cref="IServiceProvider.GetService"/> so that mock
    /// re-configurations in test environments are always honoured.
    /// </summary>
    private static readonly ConditionalWeakTable<IServiceProvider, IMemoryCache> s_caches = new();

    extension(IServiceProvider serviceProvider)
    {
        public THandler[] GetHandlers<THandler, TMessage, TResult>() where THandler : IHandler<TMessage, TResult> where TMessage : notnull where TResult : notnull
        {
            return serviceProvider.GetCachedServices<THandler>();
        }

        private T[] GetCachedServices<T>()
        {
            var serviceType = typeof(T);
            
            SetUsage(serviceType);

            // Only cache handlers when the container provides its own IMemoryCache.
            // When the container has no IMemoryCache (e.g. test environments with mock providers)
            // skip caching so that mock re-configurations between test methods are respected.
            var cache = serviceProvider.GetService<IMemoryCache>();
            if (cache is null)
                return serviceProvider.GetServices<T>().ToArray();

            return cache.GetOrCreate(serviceType, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = CalculateCachePersistence(serviceType);
                return serviceProvider.GetServices<T>().ToArray();
            }) ?? [];
        }
    }
}