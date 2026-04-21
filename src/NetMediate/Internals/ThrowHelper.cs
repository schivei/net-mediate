namespace NetMediate.Internals;

internal static class ThrowHelper
{
    public static void ThrowIfNull(
        object? argument,
        [System.Runtime.CompilerServices.CallerArgumentExpression(nameof(argument))]
            string? paramName = null
    )
    {
        if (argument is null)
            throw new ArgumentNullException(paramName);
    }
}
