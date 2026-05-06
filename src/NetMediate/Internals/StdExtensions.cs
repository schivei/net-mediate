#if NETSTANDARD2_0_OR_GREATER || NETSTANDARD2_1_OR_GREATER
namespace NetMediate.Internals;

/// <summary>
/// Provides extension methods for standard .NET types that are not available
/// in all target frameworks. These methods are implemented as backports of features
/// from newer .NET versions to ensure compatibility across different environments.
/// </summary>
public static class StdExtensions
{
    /// <summary>
    /// Concatenates two <see cref="IAsyncEnumerable{T}"/> sequences into a single sequence.
    /// The resulting sequence will yield all elements from the first sequence followed by all elements from the second sequence.
    /// </summary>
    /// <param name="first">The first sequence to concatenate.</param>
    /// <param name="second">The second sequence to concatenate.</param>
    /// <typeparam name="T">The type of elements in the sequences.</typeparam>
    /// <returns>An <see cref="IAsyncEnumerable{T}"/> that contains the concatenated elements of the input sequences.</returns>
    public static async IAsyncEnumerable<T> Concat<T>(this IAsyncEnumerable<T> first, IAsyncEnumerable<T> second)
    {
        await foreach (var item in first)
        {
            yield return item;
        }

        await foreach (var item in second)
        {
            yield return item;
        }
    }

    /// <summary>
    /// Reverses the order of the elements in a sequence.
    /// </summary>
    /// <param name="source">The sequence to reverse.</param>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <returns>An <see cref="IEnumerable{T}"/> with the elements in reverse order.</returns>
    public static IEnumerable<T> Reverse<T>(this IEnumerable<T> source) =>
        Enumerable.Reverse(source);
}
#endif
