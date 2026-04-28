using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

/// <summary>
/// A drop-in <see cref="ILogger{T}"/> wrapper that queues all log entries to an
/// unbounded <see cref="Channel{T}"/> and drains them on a dedicated background thread.
/// <para>
/// This keeps the hot-path (command/request dispatch) free from synchronous logging overhead.
/// Pre-formatting of log messages happens on the calling thread only when the log level is
/// actually enabled; the formatted string is then queued and written by the background drain
/// task so that the caller returns without waiting for any I/O.
/// </para>
/// </summary>
internal sealed class BackgroundLogger<T> : ILogger<T>, IDisposable, IAsyncDisposable
{
    private readonly record struct LogEntry(
        LogLevel Level,
        EventId EventId,
        string FormattedMessage,
        Exception? Exception);

    private readonly ILogger<T> _inner;
    private readonly Channel<LogEntry> _channel;
    private readonly Task _drainTask;

    public BackgroundLogger(ILoggerFactory factory)
    {
        _inner = factory.CreateLogger<T>();
        _channel = Channel.CreateUnbounded<LogEntry>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

        // Start a dedicated long-running drain loop that lives for the lifetime of this singleton.
        _drainTask = Task.Factory.StartNew(
            DrainLoopAsync,
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        // Guard first – avoids any allocation when the log level is not enabled.
        if (!_inner.IsEnabled(logLevel))
            return;

        // Pre-format on the calling thread so the generic state is not captured.
        // TryWrite is non-blocking; the channel is unbounded so it always succeeds.
        _channel.Writer.TryWrite(new LogEntry(logLevel, eventId, formatter(state, exception), exception));
    }

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => _inner.IsEnabled(logLevel);

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull =>
        _inner.BeginScope(state);

    private async Task DrainLoopAsync()
    {
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                _inner.Log(
                    entry.Level,
                    entry.EventId,
                    entry.FormattedMessage,
                    entry.Exception,
                    static (msg, _) => msg);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown; remaining entries may be lost.
        }
    }

    /// <summary>
    /// Completes the channel writer.  Pending entries that have not yet been drained may
    /// be lost.  Use <see cref="DisposeAsync"/> for a guaranteed flush of pending entries.
    /// </summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Completes the channel writer and awaits the drain loop so that all pending
    /// log entries are flushed before the returned <see cref="ValueTask"/> completes.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        try
        {
            await _drainTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        GC.SuppressFinalize(this);
    }
}
