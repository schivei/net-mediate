using System.Text.Json;

namespace NetMediate.Quartz;

/// <summary>
/// Default implementation of <see cref="INotificationSerializer"/> that uses <see cref="System.Text.Json.JsonSerializer"/>.
/// </summary>
public sealed class JsonNotificationSerializer : INotificationSerializer
{
    private static readonly JsonSerializerOptions s_options = new(JsonSerializerDefaults.Web);

    /// <inheritdoc />
    public string Serialize<TMessage>(TMessage message) where TMessage : notnull =>
        JsonSerializer.Serialize<object>(message, s_options);

    /// <inheritdoc />
    public object? Deserialize(string data, Type messageType) =>
        JsonSerializer.Deserialize(data, messageType, s_options);
}
