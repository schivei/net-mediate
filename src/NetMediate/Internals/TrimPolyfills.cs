#if !NET5_0_OR_GREATER
#pragma warning disable IDE0130
namespace System.Diagnostics.CodeAnalysis;

/// <summary>
/// Indicates that the specified method requires the ability to generate new code at runtime,
/// for example through <c>System.Reflection.Emit</c>. This attribute is polyfilled for pre-.NET 5 TFMs.
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
/// Indicates that the specified method requires dynamic access to code that is not referenced statically,
/// for example through <see cref="Reflection"/>. This attribute is polyfilled for pre-.NET 5 TFMs.
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

/// <summary>
/// Specifies the types of members dynamically accessed. This attribute is polyfilled for pre-.NET 5 TFMs.
/// </summary>
[Flags]
internal enum DynamicallyAccessedMemberTypes
{
    /// <summary>No members are accessed.</summary>
    None = 0x0,

    /// <summary>Default constructor is accessed.</summary>
    DefaultConstructor = 0x1,

    /// <summary>All public constructors are accessed.</summary>
    PublicConstructors = 0x3,

    /// <summary>All non-public constructors are accessed.</summary>
    NonPublicConstructors = 0x4,

    /// <summary>All public methods are accessed.</summary>
    PublicMethods = 0x8,

    /// <summary>All non-public methods are accessed.</summary>
    NonPublicMethods = 0x10,

    /// <summary>All public fields are accessed.</summary>
    PublicFields = 0x20,

    /// <summary>All non-public fields are accessed.</summary>
    NonPublicFields = 0x40,

    /// <summary>All public nested types are accessed.</summary>
    PublicNestedTypes = 0x80,

    /// <summary>All non-public nested types are accessed.</summary>
    NonPublicNestedTypes = 0x100,

    /// <summary>All public properties are accessed.</summary>
    PublicProperties = 0x200,

    /// <summary>All non-public properties are accessed.</summary>
    NonPublicProperties = 0x400,

    /// <summary>All public events are accessed.</summary>
    PublicEvents = 0x800,

    /// <summary>All non-public events are accessed.</summary>
    NonPublicEvents = 0x1000,

    /// <summary>All interfaces implemented by the type are accessed.</summary>
    Interfaces = 0x2000,

    /// <summary>All members are accessed.</summary>
    All = ~None,
}

/// <summary>
/// Indicates that certain members on a specified <see cref="Type"/> are accessed dynamically.
/// This attribute is polyfilled for pre-.NET 5 TFMs.
/// </summary>
[AttributeUsage(
    AttributeTargets.Field
        | AttributeTargets.ReturnValue
        | AttributeTargets.GenericParameter
        | AttributeTargets.Parameter
        | AttributeTargets.Property
        | AttributeTargets.Method
        | AttributeTargets.Class
        | AttributeTargets.Interface
        | AttributeTargets.Struct,
    Inherited = false
)]
internal sealed class DynamicallyAccessedMembersAttribute(
    DynamicallyAccessedMemberTypes memberTypes
) : Attribute
{
    /// <summary>Gets the <see cref="DynamicallyAccessedMemberTypes"/> that specifies the types of members dynamically accessed.</summary>
    public DynamicallyAccessedMemberTypes MemberTypes { get; } = memberTypes;
}
#pragma warning restore IDE0130
#endif
