using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests;

/// <summary>
/// Measures the raw dispatch throughput (messages per second) of the core mediator for each
/// message type — command, notification, request, and stream — with no behaviors, no resilience,
/// and no adapters registered.  Each test dispatches a fixed number of messages sequentially after
/// a warm-up phase and emits structured output lines that include the execution mode
/// (<c>execution_mode=jit</c> or <c>execution_mode=nativeaot</c>) so that results from both
/// runtimes can be compared directly.
///
/// <para>
/// <b>JIT run (standard):</b> <c>dotnet test</c><br/>
/// <b>NativeAOT run:</b> publish with <c>-p:PublishAot=true</c> and run the native binary.
/// </para>
/// </summary>
public sealed class CoreDispatchThroughputTests(ITestOutputHelper output)
{
    private static string ExecutionMode =>
        RuntimeFeature.IsDynamicCodeSupported ? "jit" : "nativeaot";

    [Fact]
    public async Task CoreCommand_DispatchThroughput()
    {
        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var tfm = AppContext.TargetFrameworkName ?? "unknown";
        var mode = ExecutionMode;

        const int ops = 50_000;

        for (var i = 0; i < 500; i++)
            await mediator.Send(new CoreCmd(i), ct);

        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++)
            await mediator.Send(new CoreCmd(i), ct);
        var elapsed = Stopwatch.GetElapsedTime(sw);

        var msgsPerSecond = ops / elapsed.TotalSeconds;

        output.WriteLine(
            $"CORE_THROUGHPUT command tfm={tfm} execution_mode={mode} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} msgs_per_second={msgsPerSecond:F0}"
        );
        output.WriteLine(
            $"LOAD_RESULT core_command tfm={tfm} execution_mode={mode} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={msgsPerSecond:F2}"
        );

        Assert.True(
            msgsPerSecond > 500,
            $"Core command throughput too low: {msgsPerSecond:F0} msgs/s [{mode}]"
        );
    }

    [Fact]
    public async Task CoreNotification_DispatchThroughput()
    {
        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var tfm = AppContext.TargetFrameworkName ?? "unknown";
        var mode = ExecutionMode;

        const int ops = 50_000;

        for (var i = 0; i < 500; i++)
            await mediator.Notify(new CoreNotif(i), ct);

        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++)
            await mediator.Notify(new CoreNotif(i), ct);
        var elapsed = Stopwatch.GetElapsedTime(sw);

        var msgsPerSecond = ops / elapsed.TotalSeconds;

        output.WriteLine(
            $"CORE_THROUGHPUT notification tfm={tfm} execution_mode={mode} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} msgs_per_second={msgsPerSecond:F0}"
        );
        output.WriteLine(
            $"LOAD_RESULT core_notification tfm={tfm} execution_mode={mode} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={msgsPerSecond:F2}"
        );

        Assert.True(
            msgsPerSecond > 500,
            $"Core notification throughput too low: {msgsPerSecond:F0} msgs/s [{mode}]"
        );
    }

    [Fact]
    public async Task CoreRequest_DispatchThroughput()
    {
        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var tfm = AppContext.TargetFrameworkName ?? "unknown";
        var mode = ExecutionMode;

        const int ops = 50_000;

        for (var i = 0; i < 500; i++)
            await mediator.Request<CoreReq, int>(new(i), ct);

        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++)
            await mediator.Request<CoreReq, int>(new(i), ct);
        var elapsed = Stopwatch.GetElapsedTime(sw);

        var msgsPerSecond = ops / elapsed.TotalSeconds;

        output.WriteLine(
            $"CORE_THROUGHPUT request tfm={tfm} execution_mode={mode} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} msgs_per_second={msgsPerSecond:F0}"
        );
        output.WriteLine(
            $"LOAD_RESULT core_request tfm={tfm} execution_mode={mode} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={msgsPerSecond:F2}"
        );

        Assert.True(
            msgsPerSecond > 500,
            $"Core request throughput too low: {msgsPerSecond:F0} msgs/s [{mode}]"
        );
    }

    [Fact]
    public async Task CoreStream_DispatchThroughput()
    {
        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;
        var tfm = AppContext.TargetFrameworkName ?? "unknown";
        var mode = ExecutionMode;

        const int ops = 10_000;

        for (var i = 0; i < 200; i++)
            await foreach (var _ in mediator.RequestStream<CoreStream, int>(new(i), ct)) { }

        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++)
            await foreach (var _ in mediator.RequestStream<CoreStream, int>(new(i), ct)) { }
        var elapsed = Stopwatch.GetElapsedTime(sw);

        var msgsPerSecond = ops / elapsed.TotalSeconds;

        output.WriteLine(
            $"CORE_THROUGHPUT stream tfm={tfm} execution_mode={mode} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} msgs_per_second={msgsPerSecond:F0}"
        );
        output.WriteLine(
            $"LOAD_RESULT core_stream tfm={tfm} execution_mode={mode} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={msgsPerSecond:F2}"
        );

        Assert.True(
            msgsPerSecond > 500,
            $"Core stream throughput too low: {msgsPerSecond:F0} msgs/s [{mode}]"
        );
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.UseNetMediate(configure =>
        {
            configure.RegisterCommandHandler<CoreCmdHandler, CoreCmd>();
            configure.RegisterNotificationHandler<CoreNotifHandler, CoreNotif>();
            configure.RegisterRequestHandler<CoreReqHandler, CoreReq, int>();
            configure.RegisterStreamHandler<CoreStreamHandler, CoreStream, int>();
        });

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    public sealed record CoreCmd(int Value);

    public sealed record CoreNotif(int Value);

    public sealed record CoreReq(int Value);

    public sealed record CoreStream(int Value);

    private sealed class CoreCmdHandler : ICommandHandler<CoreCmd>
    {
        public Task Handle(CoreCmd command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CoreNotifHandler : INotificationHandler<CoreNotif>
    {
        public Task Handle(CoreNotif notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class CoreReqHandler : IRequestHandler<CoreReq, int>
    {
        public Task<int> Handle(CoreReq query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Value + 1);
    }

    private sealed class CoreStreamHandler : IStreamHandler<CoreStream, int>
    {
        public async IAsyncEnumerable<int> Handle(
            CoreStream request,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            for (var i = 0; i < 3; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return request.Value + i;
                await Task.Yield();
            }
        }
    }
}
