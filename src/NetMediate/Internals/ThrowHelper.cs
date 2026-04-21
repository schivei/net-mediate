namespace NetMediate.Internals;

internal static class ThrowHelper
{
    public static void ThrowIfNull(object? argument, string? paramName = null)
    {
        if (argument is null)
            throw new ArgumentNullException(paramName);
    }
}
