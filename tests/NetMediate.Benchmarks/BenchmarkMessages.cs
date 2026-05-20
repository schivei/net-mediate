using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Benchmarks;

/// <summary>Benchmark command message.</summary>
public sealed record BenchCommand;

/// <summary>No-op command handler used in benchmarks.</summary>
[Injectable(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None)]
public sealed class BenchCommandHandler : ICommandHandler<BenchCommand>
{
    /// <inheritdoc/>
    public ValueTask Handle(BenchCommand message, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

/// <summary>Benchmark notification message.</summary>
public sealed record BenchNotification(int Value);

/// <summary>No-op notification handler used in benchmarks.</summary>
[Injectable(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None)]
public sealed class BenchNotificationHandler : INotificationHandler<BenchNotification>
{
    /// <inheritdoc/>
    public ValueTask Handle(BenchNotification message, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

/// <summary>Benchmark request message.</summary>
public sealed record BenchRequest(int Value);

/// <summary>Benchmark request response.</summary>
public sealed record BenchResponse(int Value);

/// <summary>No-op request handler used in benchmarks.</summary>
[Injectable(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None)]
public sealed class BenchRequestHandler : IRequestHandler<BenchRequest, BenchResponse>
{
    private static readonly ValueTask<BenchResponse> s_response = new(new BenchResponse(42));

    /// <inheritdoc/>
    public ValueTask<BenchResponse> Handle(
        BenchRequest message,
        CancellationToken cancellationToken = default
    ) => s_response;
}

/// <summary>Benchmark stream message.</summary>
[Injectable(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None)]
public sealed record BenchStreamRequest;

/// <summary>Benchmark stream item.</summary>
public sealed record BenchStreamItem(int Index);

/// <summary>No-op stream handler that yields three items, used in benchmarks.</summary>
[Injectable(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None)]
public sealed class BenchStreamHandler : IStreamHandler<BenchStreamRequest, BenchStreamItem>
{
    private readonly BenchStreamItem[] _items =
    [
        new(1),
        new(2),
        new(3),
    ];

    /// <inheritdoc/>
    public async IAsyncEnumerable<BenchStreamItem> Handle(
        BenchStreamRequest message,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
    )
    {
        foreach (var item in _items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
        }
    }
}
