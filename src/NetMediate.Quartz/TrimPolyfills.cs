#if !NET5_0_OR_GREATER
#pragma warning disable IDE0130 
namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Indicates that the specified member requires the ability to generate new code at runtime.
/// This attribute is polyfilled for pre-.NET 5 TFMs.
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class,
    Inherited = false
)]
internal sealed class RequiresDynamicCodeAttribute(string message) : Attribute
{
    /// <summary>Gets the message that describes the usage.</summary>
    public string Message { get; } = message;

    /// <summary>Gets or sets an optional URL that contains more information about the member.</summary>
    public string? Url { get; set; }
}

/// <summary>
/// Indicates that the specified member requires dynamic access to code that is not referenced statically.
/// This attribute is polyfilled for pre-.NET 5 TFMs.
/// </summary>
[AttributeUsage(
    AttributeTargets.Method | AttributeTargets.Constructor | AttributeTargets.Class,
    Inherited = false
)]
internal sealed class RequiresUnreferencedCodeAttribute(string message) : Attribute
{
    /// <summary>Gets the message that describes the usage.</summary>
    public string Message { get; } = message;

    /// <summary>Gets or sets an optional URL that contains more information about the member.</summary>
    public string? Url { get; set; }
}
#pragma warning restore IDE0130
#endif
