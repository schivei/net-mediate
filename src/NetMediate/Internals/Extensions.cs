using System.Reflection;
using System.Threading.Channels;

namespace NetMediate.Internals;

internal static class Extensions
{
    public static async ValueTask DrainAsync<T>(this ChannelReader<T> channel)
    {
        await foreach (var _ in channel.ReadAllAsync().ConfigureAwait(false)) ;
    }

    public static string? GetKey(this Type type) =>
        type.GetCustomAttribute<KeyedMessageAttribute>(false)?.ServiceKey;
}
