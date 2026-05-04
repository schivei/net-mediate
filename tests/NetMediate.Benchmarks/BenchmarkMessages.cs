// Dedicated message types — one per benchmark class — so DI handler and behavior caches
// never share entries between benchmark methods.
namespace NetMediate.Benchmarks;

// ── Command ──────────────────────────────────────────────────────────────────
/// <summary>Benchmark command message.</summary>
public sealed record BenchCommand;

/// <summary>No-op command handler used in benchmarks.</summary>
public sealed class BenchCommandHandler : ICommandHandler<BenchCommand>
{
    /// <inheritdoc/>
    public Task Handle(BenchCommand message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// ── Notification ─────────────────────────────────────────────────────────────
/// <summary>Benchmark notification message.</summary>
public sealed record BenchNotification;

/// <summary>No-op notification handler used in benchmarks.</summary>
public sealed class BenchNotificationHandler : INotificationHandler<BenchNotification>
{
    /// <inheritdoc/>
    public Task Handle(BenchNotification message, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

// ── Request ───────────────────────────────────────────────────────────────────
/// <summary>Benchmark request message.</summary>
public sealed record BenchRequest;

/// <summary>Benchmark request response.</summary>
public sealed record BenchResponse(int Value);

/// <summary>No-op request handler used in benchmarks.</summary>
public sealed class BenchRequestHandler : IRequestHandler<BenchRequest, BenchResponse>
{
    private static readonly Task<BenchResponse> s_response = Task.FromResult(new BenchResponse(42));

    /// <inheritdoc/>
    public Task<BenchResponse> Handle(BenchRequest message, CancellationToken cancellationToken = default)
        => s_response;
}

// ── Stream ────────────────────────────────────────────────────────────────────
/// <summary>Benchmark stream message.</summary>
public sealed record BenchStreamRequest;

/// <summary>Benchmark stream item.</summary>
public sealed record BenchStreamItem(int Index);

/// <summary>No-op stream handler that yields three items, used in benchmarks.</summary>
public sealed class BenchStreamHandler : IStreamHandler<BenchStreamRequest, BenchStreamItem>
{
    /// <inheritdoc/>
    public async IAsyncEnumerable<BenchStreamItem> Handle(
        BenchStreamRequest message,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        yield return new BenchStreamItem(1);
        yield return new BenchStreamItem(2);
        yield return new BenchStreamItem(3);
    }
}
