#if NETSTANDARD2_1
namespace System;

internal static class ArgumentNullExceptionCompat
{
    public static void ThrowIfNull(object? argument, string? paramName = null)
    {
        if (argument is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }
}
#endif