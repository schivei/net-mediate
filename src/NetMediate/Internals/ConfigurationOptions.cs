using System.Threading.Channels;
using Microsoft.Extensions.Caching.Memory;

namespace NetMediate.Internals;

internal sealed class ConfigurationOptions(Channel<IPack> channel)
{
    public Channel<IPack> Channel => channel;
    
    public bool IgnoreUnhandledMessages { get; set; }
    
    public IMemoryCache Cache { get; set; } = new MemoryCache(new MemoryCacheOptions());
}