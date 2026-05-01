using System.Threading.Channels;

namespace NetMediate.Internals;

internal sealed class Configuration(Channel<IPack> channel) : IAsyncDisposable
{
    private bool _disposed;

    public ChannelWriter<IPack> ChannelWriter => channel.Writer;
    public ChannelReader<IPack> ChannelReader => channel.Reader;
    public bool IgnoreUnhandledMessages { get; set; }
    public bool Disposed => _disposed;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        _disposed = true;

        await channel.DrainAsync();

        GC.SuppressFinalize(this);
    }
}
