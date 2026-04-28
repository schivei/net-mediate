using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal sealed class Configuration(Channel<INotificationPacket> channel) : IAsyncDisposable
{
    private readonly Dictionary<Type, Func<object, Type?>> _filters = [];

    // Tracks which message types have at least one IValidationHandler<T> registered.
    // Populated at startup by MediatorServiceBuilder; checked at dispatch time to skip
    // the validation DI resolution entirely when no validators are registered.
    private readonly HashSet<Type> _validatableTypes = [];

    // Tracks which message types implement IValidatable (self-validating).
    // Populated lazily and cached to avoid repeated IsAssignableFrom calls.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, bool> s_selfValidatableCache = new();

    public ChannelWriter<INotificationPacket> ChannelWriter => channel.Writer;
    public ChannelReader<INotificationPacket> ChannelReader => channel.Reader;
    public bool IgnoreUnhandledMessages { get; set; }
    public bool LogUnhandledMessages { get; set; }
    public LogLevel UnhandledMessagesLogLevel { get; set; }

    /// <summary>Marks a message type as requiring validation (has at least one registered <see cref="IValidationHandler{TMessage}"/>).</summary>
    internal void MarkAsValidatable(Type messageType) => _validatableTypes.Add(messageType);

    /// <summary>
    /// Returns <see langword="true"/> if the message type has registered validation handlers
    /// OR implements <see cref="IValidatable"/> (self-validation).
    /// </summary>
    internal bool NeedsValidation<TMessage>(TMessage? message)
    {
        var type = typeof(TMessage);
        return _validatableTypes.Contains(type)
            || s_selfValidatableCache.GetOrAdd(type, static t => typeof(IValidatable).IsAssignableFrom(t));
    }

    public async ValueTask DisposeAsync()
    {
        await channel.DrainAsync();

        GC.SuppressFinalize(this);
    }

    public void InstantiateHandlerByMessageFilter<TMessage>(Func<TMessage, Type?> filter)
    {
        ThrowHelper.ThrowIfNull(filter);

        _filters[typeof(TMessage)] = message => filter((TMessage)message);
    }

    public bool TryGetHandlerTypeByMessageFilter<TMessage>(TMessage message, out Type? handlerType)
    {
        handlerType = null;

        if (_filters.TryGetValue(typeof(TMessage), out var filter))
        {
            handlerType = filter(message);

            return true;
        }

        return false;
    }
}
