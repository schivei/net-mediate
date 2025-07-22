using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace NetMediate.Internals;

internal sealed class Configuration(Channel<object> channel) : IAsyncDisposable
{
    private readonly Dictionary<Type, Func<dynamic, Type?>> _filters = [];
    public ChannelWriter<object> ChannelWriter => channel.Writer;
    public ChannelReader<object> ChannelReader => channel.Reader;
    public bool IgnoreUnhandledMessages { get; set; }
    public bool LogUnhandledMessages { get; set; }
    public LogLevel UnhandledMessagesLogLevel { get; set; }

    public async ValueTask DisposeAsync()
    {
        ChannelWriter.TryComplete();

        await ChannelReader.DrainAsync();

        GC.SuppressFinalize(this);
    }

    public void InstantiateHandlerByMessageFilter<TMessage>(Func<TMessage, Type?> filter)
    {
        ArgumentNullException.ThrowIfNull(filter);

        _filters[typeof(TMessage)] = message => filter(message);
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
