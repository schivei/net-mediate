namespace NetMediate.Diagnostics.Tests;

internal static class AsyncEnumerableExtension
{
    public static async Task Drain<T>(this IAsyncEnumerable<T> data)
    {
        await foreach (var d in data.ConfigureAwait(true))
        {
            if (d is Exception ex)
                throw ex;
        }
    }
}
