using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Benchmarks;

/// <summary>
/// Core dispatch throughput benchmarks — no pipeline behaviors, no resilience, no adapters.
/// Measures the raw overhead of the mediator dispatch path for each message type.
/// </summary>
/// <remarks>
/// Run with:
/// <code>
///   dotnet run -c Release --project tests/NetMediate.Benchmarks/
/// </code>
/// For NativeAOT comparison:
/// <code>
///   dotnet publish tests/NetMediate.Benchmarks/ -c Release -p:AotBenchmark=true -o /tmp/bench-aot
///   /tmp/bench-aot/NetMediate.Benchmarks
/// </code>
/// </remarks>
[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput)]
public class CoreDispatchBenchmarks
{
    private IMediator _mediator = null!;
    private ServiceProvider _provider = null!;

    private static readonly BenchCommand s_command = new();
    private static readonly BenchNotification s_notification = new();
    private static readonly BenchRequest s_request = new();
    private static readonly BenchStreamRequest s_streamRequest = new();

    /// <summary>Sets up the DI container and resolves the mediator once before all iterations.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.UseNetMediate(configure =>
        {
            configure.RegisterCommandHandler<BenchCommandHandler, BenchCommand>();
            configure.RegisterNotificationHandler<BenchNotificationHandler, BenchNotification>();
            configure.RegisterRequestHandler<BenchRequestHandler, BenchRequest, BenchResponse>();
            configure.RegisterStreamHandler<BenchStreamHandler, BenchStreamRequest, BenchStreamItem>();
        });

        _provider = services.BuildServiceProvider();
        _mediator = _provider.GetRequiredService<IMediator>();

        // Warm up — primes the handler and behavior caches so the first measured call
        // does not include one-time DI resolution overhead.
        _mediator.Send(s_command).GetAwaiter().GetResult();
        _mediator.Notify(s_notification).GetAwaiter().GetResult();
        _mediator.Request<BenchRequest, BenchResponse>(s_request).GetAwaiter().GetResult();
        DrainStream(_mediator.RequestStream<BenchStreamRequest, BenchStreamItem>(s_streamRequest)).GetAwaiter().GetResult();
    }

    /// <summary>Tears down the DI container after all iterations.</summary>
    [GlobalCleanup]
    public void Cleanup() => _provider.Dispose();

    private const int OpsPerInvoke = 1_000;

    /// <summary>Measures the per-call overhead of <see cref="IMediator.Send{TMessage}"/>.</summary>
    [Benchmark(Description = "Command  Send", OperationsPerInvoke = OpsPerInvoke)]
    public async Task Command()
    {
        for (int i = 0; i < OpsPerInvoke; i++)
            await _mediator.Send(s_command);
    }

    /// <summary>Measures the per-call overhead of <see cref="IMediator.Notify{TMessage}"/>.</summary>
    [Benchmark(Description = "Notification  Notify", OperationsPerInvoke = OpsPerInvoke)]
    public async Task Notification()
    {
        for (int i = 0; i < OpsPerInvoke; i++)
            await _mediator.Notify(s_notification);
    }

    /// <summary>Measures the per-call overhead of <see cref="IMediator.Request{TMessage,TResponse}"/>.</summary>
    [Benchmark(Description = "Request  Request", OperationsPerInvoke = OpsPerInvoke)]
    public async Task Request()
    {
        for (int i = 0; i < OpsPerInvoke; i++)
            await _mediator.Request<BenchRequest, BenchResponse>(s_request);
    }

    /// <summary>
    /// Measures the end-to-end cost of a single stream invocation including draining all
    /// yielded items.  Each invocation yields 3 items.
    /// </summary>
    [Benchmark(Description = "Stream  RequestStream (3 items/call)", OperationsPerInvoke = OpsPerInvoke)]
    public async Task Stream()
    {
        for (int i = 0; i < OpsPerInvoke; i++)
            await DrainStream(_mediator.RequestStream<BenchStreamRequest, BenchStreamItem>(s_streamRequest));
    }

    private static async Task DrainStream(IAsyncEnumerable<BenchStreamItem> stream)
    {
        await foreach (var _ in stream) { } // NOSONAR S108
    }
}
