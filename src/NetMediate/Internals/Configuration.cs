using System.Threading.Channels;

namespace NetMediate.Internals;

internal sealed class Configuration(Channel<IPack> channel) : IAsyncDisposable
{
    private int _disposed;

    public ChannelWriter<IPack> ChannelWriter => channel.Writer;
    public ChannelReader<IPack> ChannelReader => channel.Reader;
    public bool IgnoreUnhandledMessages { get; set; }
    public bool Disposed => Interlocked.CompareExchange(ref _disposed, 0, 0) == 1;

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await channel.DrainAsync();

        GC.SuppressFinalize(this);
    }
}
