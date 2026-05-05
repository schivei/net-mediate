namespace NetMediate;

/// <summary>
/// Specifies a key that can be used to identify or distinguish a service implementation for dependency injection or
/// service location.
/// </summary>
/// <remarks>Apply this attribute to a class to associate it with a specific key, enabling keyed resolution
/// scenarios in dependency injection frameworks or custom service locators. The key can be any object and is typically
/// used to differentiate between multiple implementations of the same service contract.</remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class KeyedServiceAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the key associated with the current object.
    /// </summary>
    public object? Key { get; set; } = null;
}
