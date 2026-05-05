namespace NetMediate;

/// <summary>
/// Specifies the relative order used when NetMediate source-generated handler registrations are emitted for a class.
/// </summary>
/// <remarks>
/// Apply this attribute to handler classes when using NetMediate's source-generated registration support and registration
/// order matters. Lower values are registered first. This attribute does not, by itself, affect manual registration paths
/// or user-defined pipeline behavior ordering.
/// </remarks>
/// <param name="order">The relative order to use for source-generated registration. Lower values indicate higher priority.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ServiceOrderAttribute(int order) : Attribute
{
    /// <summary>
    /// Gets the relative order used for source-generated registration.
    /// </summary>
    public int Order { get; } = order;
}
