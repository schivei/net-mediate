using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Internals;

internal static class ThrowHelper
{
#if NETSTANDARD2_1
    public static void ThrowIfNull([NotNull] object? argument, string? paramName = null)
    {
        if (argument is null)
            throw new ArgumentNullException(paramName);
    }
#else
    public static void ThrowIfNull([NotNull] object? argument, [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(argument))] string? paramName = null)
    {
        ArgumentNullException.ThrowIfNull(argument, paramName);
    }
#endif
}