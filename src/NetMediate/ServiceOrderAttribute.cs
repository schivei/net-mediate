namespace NetMediate;

/// <summary>
/// Specifies the processing or application order for a class when multiple services are present.
/// </summary>
/// <remarks>Apply this attribute to service classes to control their execution or registration order in scenarios
/// where order matters, such as dependency injection pipelines or service initialization.</remarks>
/// <param name="order">The relative order in which the attributed class should be processed or applied. Lower values indicate higher
/// priority.</param>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class ServiceOrderAttribute(int order) : Attribute
{
    /// <summary>
    /// Gets the order in which this element is processed or applied.
    /// </summary>
    public int Order { get; } = order;
}
