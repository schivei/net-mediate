namespace NetMediate;

/// <summary>
/// Specifies a service key for a message type, allowing keyed registration and resolution.
/// </summary>
/// <remarks>
/// Apply this attribute to a class or struct to associate it with a specific service key.
/// This is useful for scenarios where multiple message types are handled differently based on a key.
/// </remarks>
/// <param name="serviceKey">
/// The unique key associated with the message type.
/// </param>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct,
    Inherited = false,
    AllowMultiple = false
)]
public sealed class KeyedMessageAttribute(string serviceKey) : Attribute
{
    /// <summary>
    /// Gets the service key associated with the message type.
    /// </summary>
    public string ServiceKey => serviceKey;
}
