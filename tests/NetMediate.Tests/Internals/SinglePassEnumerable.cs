using System.Collections;

namespace NetMediate.Tests.Internals;

internal sealed class SinglePassEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
{
    private bool _enumerated;

    public IEnumerator<T> GetEnumerator()
    {
        if (_enumerated)
            throw new InvalidOperationException("Sequence can only be enumerated once.");

        _enumerated = true;
        return values.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
