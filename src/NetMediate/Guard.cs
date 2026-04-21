namespace NetMediate;

/// <summary>
/// Provides guard helper methods for argument validation across framework targets.
/// </summary>
internal static class Guard
{
    /// <summary>
    /// Throws <see cref="ArgumentNullException"/> when the provided argument is null.
    /// </summary>
    /// <param name="argument">Argument instance to validate.</param>
    /// <param name="paramName">Optional argument name.</param>
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
