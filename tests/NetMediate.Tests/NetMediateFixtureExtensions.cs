
namespace NetMediate.Tests;

public static class NetMediateFixtureExtensions
{
    public static async Task<IEnumerable<T>> AsyncToSync<T>(this IAsyncEnumerable<T> values)
    {
        var list = new List<T>();
        await foreach (var item in values)
            list.Add(item);

        return list;
    }
}
