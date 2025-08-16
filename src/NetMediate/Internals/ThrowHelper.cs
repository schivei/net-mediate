using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Internals;

internal static class ThrowHelper
{
    public static void ThrowIfNull(
        [NotNull] object? argument,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(argument))]
            string? paramName = null
    )
    {
        ArgumentNullException.ThrowIfNull(argument, paramName);
    }
}
