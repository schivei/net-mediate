using System.Reflection;
using System.Threading.Channels;

namespace NetMediate.Internals;

internal static class Extensions
{
    public static async ValueTask DrainAsync<T>(this Channel<T> channel)
    {
        channel.Writer.TryComplete();

        await foreach (var _ in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            /* ignore */;
    }

    public static string? GetKey(this Type type) =>
        type.GetCustomAttribute<KeyedMessageAttribute>(false)?.ServiceKey;
}
