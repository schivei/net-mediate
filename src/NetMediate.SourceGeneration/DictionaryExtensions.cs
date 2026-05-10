using System.Collections.Concurrent;

namespace NetMediate.SourceGeneration;

internal static class DictionaryExtensions
{
    /// <summary>
    /// Inserts <paramref name="key"/> with a <c>true</c> value if the key is not already present.
    /// Provides insertion-ordered deduplication on <c>netstandard2.0</c> where
    /// <see cref="Dictionary{TKey,TValue}.TryAdd"/> is unavailable.
    /// </summary>
    internal static void AddIfNew(this Dictionary<string, bool> dict, string key)
    {
        if (!dict.ContainsKey(key))
            dict[key] = true;
    }

    /// <summary>
    /// Inserts <paramref name="key"/> with a <c>true</c> value if the key is not already present.
    /// Provides insertion-ordered deduplication on <c>netstandard2.0</c> where
    /// <see cref="Dictionary{TKey,TValue}.TryAdd"/> is unavailable.
    /// </summary>
    internal static void AddIfNew(this ConcurrentDictionary<string, bool> dict, string key)
    {
        if (!dict.ContainsKey(key))
            dict[key] = true;
    }
}
