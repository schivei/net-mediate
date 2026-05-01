using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;

namespace NetMediate.Internals;

internal static class Extensions
{
    public static async ValueTask DrainAsync<T>(this Channel<T> channel)
    {
        channel.Writer.TryComplete();

        await foreach (var _ in channel.Reader.ReadAllAsync().ConfigureAwait(false)) { }
    }

    public static IEnumerable<T> GetAllServices<T>(this IServiceProvider serviceProvider)
    {
        try
        {
            return serviceProvider is IServiceProviderIsService isService && !isService.IsService(typeof(T)) && !isService.IsService(typeof(IEnumerable<T>))
            ? []
            : serviceProvider.GetServices<T>();
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
