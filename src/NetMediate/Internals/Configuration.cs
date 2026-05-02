using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace NetMediate.Internals;

internal sealed class Configuration(IOptions<ConfigurationOptions> options, IMemoryCache cache, IServiceProvider serviceProvider) : IDisposable
{
    private int _disposed;

    public IMemoryCache Cache
    {
        get
        {
            if (field is not null)
                return field;
            
            return field = options.Value.Cache = cache;
        }
    } = null!;

    public ChannelWriter<IPack> ChannelWriter => options.Value.Channel.Writer;
    public ChannelReader<IPack> ChannelReader => options.Value.Channel.Reader;
    public bool IgnoreUnhandledMessages => options.Value.IgnoreUnhandledMessages;
    public bool Disposed => Interlocked.CompareExchange(ref _disposed, 0, 0) == 1;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        options.Value.Channel.Drain();
    }
    
    public IEnumerable<IHandler> GetHandlers(Type messageType)
    {
        var handlerType = typeof(IHandler<,>).MakeGenericType(messageType, typeof(object));
        return serviceProvider.GetCachedServices(handlerType).Cast<IHandler>();
    }
}
