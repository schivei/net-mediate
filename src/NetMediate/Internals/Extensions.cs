using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;

namespace NetMediate.Internals;

internal static class Extensions
{
    private static readonly ConcurrentDictionary<Type, ulong> s_serviceUsage = [];
    
    private static void SetUsage(Type serviceType)
    {
        s_serviceUsage.AddOrUpdate(serviceType, 1, (_, count) => count + 1);
    }

    private static TimeSpan CalculateCachePersistence(Type serviceType)
    {
        var timeLimit = TimeSpan.FromMinutes(5);
        if (s_serviceUsage.TryGetValue(serviceType, out var count))
        {
            timeLimit = count * timeLimit;
        }
        
        if (timeLimit <= TimeSpan.Zero)
            timeLimit = TimeSpan.FromMinutes(5);
        
        if (timeLimit > TimeSpan.FromHours(1))
            timeLimit = TimeSpan.FromHours(1);
        
        return timeLimit;
    }
    
    public static void Drain<T>(this Channel<T> channel)
    {
        channel.Writer.TryComplete();

        while (channel.Reader.TryPeek(out _)) ;
    }

    extension(IServiceProvider serviceProvider)
    {
        public IEnumerable<T> GetCachedServices<T>() =>
            serviceProvider.GetCachedServices(typeof(T)).Cast<T>();
        
        public IEnumerable<object> GetCachedServices(Type serviceType)
        {
            var cache = serviceProvider.GetService<IMemoryCache>() ??
                        new MemoryCache(new MemoryCacheOptions());
            
            return cache.GetOrCreate(serviceType, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
                return serviceProvider.GetAllServices(serviceType).ToArray();
            });
        }
        
        private IEnumerable<object> GetAllServices(Type serviceType)
        {
            try
            {
                // ReSharper disable once SuspiciousTypeConversion.Global
                return serviceProvider is IServiceProviderIsService isService && !isService.IsService(serviceType) && !isService.IsService(typeof(IEnumerable<>).MakeGenericType(serviceType))
                    ? []
                    : serviceProvider.GetServices(serviceType);
            }
            catch (ObjectDisposedException)
            {
                // Service provider was disposed - return empty collection
                return [];
            }
            catch (InvalidOperationException)
            {
                // Service resolution failed - return empty collection
                return [];
            }
        }
    }

    private static ValueTask<ValidationResult> SelfMessageValidation<TMessage>(TMessage message, CancellationToken cancellationToken)
    {
        return message is IValidatable validatable
            ? validatable.ValidateAsync(cancellationToken)
            : new ValueTask<ValidationResult>(ValidationResult.Success);
    }
    
    public static ValueTask<ValidationResult> ValidateAsync<TMessage>(TMessage message, MessageValidationDelegate<TMessage>? validator, CancellationToken cancellationToken)
    {
        var validation = validator ?? SelfMessageValidation;
        return validation(message, cancellationToken);
    }
}
