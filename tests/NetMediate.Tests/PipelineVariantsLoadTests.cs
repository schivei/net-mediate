using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests;

// ─────────────────────────────────────────────────────────────────────────────
// Pipeline Variants Load Tests
//
// Isolates the per-call overhead of individual pipeline features so that the
// cost of each can be measured independently:
//
//  Section A  Validation variants
//             Plain message (no IValidatable, no IValidationHandler)
//             vs. message that implements IValidatable (self-validation)
//             vs. message with an IValidationHandler registered externally
//
//  Section B  Behavior/middleware variants
//             No behaviors registered
//             vs. 1 no-op behavior
//             vs. 2 no-op behaviors stacked
//
//  Section C  Handler fan-out variants
//             1 handler per message
//             vs. 2 handlers per message
//             vs. 3 handlers per message
//
// Every test uses explicit (AOT-safe) DI registration and its own unique message
// type so that handlers and behaviors never bleed across test cases.
// ─────────────────────────────────────────────────────────────────────────────
public sealed class PipelineVariantsLoadTests(ITestOutputHelper output)
{
    // =========================================================================
    // Section A — Validation variants
    // =========================================================================

    #region A1  Command – no validation (baseline)

    [Fact]
    public async Task Command_NoValidation_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdNoValid>, CmdNoValidHandler>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Send(new CmdNoValid(i), ct);
        Emit("variant_cmd_no_valid", tfm, ops, sw);
    }

    #endregion

    #region A2  Command – self-validation (IValidatable on the message record)

    [Fact]
    public async Task Command_SelfValidation_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdSelfValid>, CmdSelfValidHandler>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Send(new CmdSelfValid(i), ct);
        Emit("variant_cmd_self_valid", tfm, ops, sw);
    }

    #endregion

    #region A3  Command – external IValidationHandler

    [Fact]
    public async Task Command_HandlerValidation_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdHandlerValid>, CmdHandlerValidHandler>();
            svc.AddSingleton<IValidationHandler<CmdHandlerValid>, CmdHandlerValidValidator>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Send(new CmdHandlerValid(i), ct);
        Emit("variant_cmd_handler_valid", tfm, ops, sw);
    }

    #endregion

    #region A4  Notification – no validation (baseline)

    [Fact]
    public async Task Notification_NoValidation_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifNoValid>, NotifNoValidHandler>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Notify(new NotifNoValid(i), ct);
        Emit("variant_notif_no_valid", tfm, ops, sw);
    }

    #endregion

    #region A5  Notification – self-validation (IValidatable)

    [Fact]
    public async Task Notification_SelfValidation_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifSelfValid>, NotifSelfValidHandler>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Notify(new NotifSelfValid(i), ct);
        Emit("variant_notif_self_valid", tfm, ops, sw);
    }

    #endregion

    #region A6  Notification – external IValidationHandler

    [Fact]
    public async Task Notification_HandlerValidation_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifHandlerValid>, NotifHandlerValidHandler>();
            svc.AddSingleton<IValidationHandler<NotifHandlerValid>, NotifHandlerValidValidator>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Notify(new NotifHandlerValid(i), ct);
        Emit("variant_notif_handler_valid", tfm, ops, sw);
    }

    #endregion

    // =========================================================================
    // Section B — Behavior / pipeline-middleware variants
    // =========================================================================

    #region B1  Command – no behaviors (parallel baseline)

    [Fact]
    public async Task Command_NoBehavior_ShouldSustainThroughputInParallel()
    {
        if (!ShouldRun()) return;
        const int ops = 10_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdNoBeh>, CmdNoBehHandler>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        await RunParallelAsync(ops, ct, i => mediator.Send(new CmdNoBeh(i), ct));
        Emit("variant_cmd_no_beh_parallel", tfm, ops, sw);
    }

    #endregion

    #region B2  Command – 1 no-op behavior (parallel)

    [Fact]
    public async Task Command_OneBehavior_ShouldSustainThroughputInParallel()
    {
        if (!ShouldRun()) return;
        const int ops = 10_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdOneBeh>, CmdOneBehHandler>();
            svc.AddSingleton<IPipelineBehavior<CmdOneBeh, Task>, NoOpCommandBehavior<CmdOneBeh>>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        await RunParallelAsync(ops, ct, i => mediator.Send(new CmdOneBeh(i), ct));
        Emit("variant_cmd_1beh_parallel", tfm, ops, sw);
    }

    #endregion

    #region B3  Command – 2 no-op behaviors stacked (parallel)

    [Fact]
    public async Task Command_TwoBehaviors_ShouldSustainThroughputInParallel()
    {
        if (!ShouldRun()) return;
        const int ops = 10_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdTwoBeh>, CmdTwoBehHandler>();
            svc.AddSingleton<IPipelineBehavior<CmdTwoBeh, Task>, NoOpCommandBehavior<CmdTwoBeh>>();
            svc.AddSingleton<IPipelineBehavior<CmdTwoBeh, Task>, NoOpCommandBehavior2<CmdTwoBeh>>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        await RunParallelAsync(ops, ct, i => mediator.Send(new CmdTwoBeh(i), ct));
        Emit("variant_cmd_2beh_parallel", tfm, ops, sw);
    }

    #endregion

    #region B4  Notification – no behaviors (parallel baseline)

    [Fact]
    public async Task Notification_NoBehavior_ShouldSustainThroughputInParallel()
    {
        if (!ShouldRun()) return;
        const int ops = 10_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifNoBeh>, NotifNoBehHandler>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        await RunParallelAsync(ops, ct, i => mediator.Notify(new NotifNoBeh(i), ct));
        Emit("variant_notif_no_beh_parallel", tfm, ops, sw);
    }

    #endregion

    #region B5  Notification – 1 no-op behavior (parallel)

    [Fact]
    public async Task Notification_OneBehavior_ShouldSustainThroughputInParallel()
    {
        if (!ShouldRun()) return;
        const int ops = 10_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifOneBeh>, NotifOneBehHandler>();
            svc.AddSingleton<IPipelineBehavior<NotifOneBeh, Task>, NoOpNotificationBehavior<NotifOneBeh>>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        await RunParallelAsync(ops, ct, i => mediator.Notify(new NotifOneBeh(i), ct));
        Emit("variant_notif_1beh_parallel", tfm, ops, sw);
    }

    #endregion

    #region B6  Notification – 2 no-op behaviors stacked (parallel)

    [Fact]
    public async Task Notification_TwoBehaviors_ShouldSustainThroughputInParallel()
    {
        if (!ShouldRun()) return;
        const int ops = 10_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifTwoBeh>, NotifTwoBehHandler>();
            svc.AddSingleton<IPipelineBehavior<NotifTwoBeh, Task>, NoOpNotificationBehavior<NotifTwoBeh>>();
            svc.AddSingleton<IPipelineBehavior<NotifTwoBeh, Task>, NoOpNotificationBehavior2<NotifTwoBeh>>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        await RunParallelAsync(ops, ct, i => mediator.Notify(new NotifTwoBeh(i), ct));
        Emit("variant_notif_2beh_parallel", tfm, ops, sw);
    }

    #endregion

    // =========================================================================
    // Section C — Handler fan-out variants
    // =========================================================================

    #region C1  Command – 1 handler (sequential baseline)

    [Fact]
    public async Task Command_OneHandler_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdFanout1>, CmdFanout1HandlerA>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Send(new CmdFanout1(i), ct);
        Emit("variant_cmd_1h", tfm, ops, sw);
    }

    #endregion

    #region C2  Command – 2 handlers (fan-out 2, sequential)

    [Fact]
    public async Task Command_TwoHandlers_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdFanout2>, CmdFanout2HandlerA>();
            svc.AddSingleton<ICommandHandler<CmdFanout2>, CmdFanout2HandlerB>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Send(new CmdFanout2(i), ct);
        Emit("variant_cmd_2h", tfm, ops, sw);
    }

    #endregion

    #region C3  Command – 3 handlers (fan-out 3, sequential)

    [Fact]
    public async Task Command_ThreeHandlers_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<ICommandHandler<CmdFanout3>, CmdFanout3HandlerA>();
            svc.AddSingleton<ICommandHandler<CmdFanout3>, CmdFanout3HandlerB>();
            svc.AddSingleton<ICommandHandler<CmdFanout3>, CmdFanout3HandlerC>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Send(new CmdFanout3(i), ct);
        Emit("variant_cmd_3h", tfm, ops, sw);
    }

    #endregion

    #region C4  Notification – 1 handler (sequential baseline)

    [Fact]
    public async Task Notification_OneHandler_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifFanout1>, NotifFanout1HandlerA>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Notify(new NotifFanout1(i), ct);
        Emit("variant_notif_1h", tfm, ops, sw);
    }

    #endregion

    #region C5  Notification – 2 handlers (fan-out 2, sequential)

    [Fact]
    public async Task Notification_TwoHandlers_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifFanout2>, NotifFanout2HandlerA>();
            svc.AddSingleton<INotificationHandler<NotifFanout2>, NotifFanout2HandlerB>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Notify(new NotifFanout2(i), ct);
        Emit("variant_notif_2h", tfm, ops, sw);
    }

    #endregion

    #region C6  Notification – 3 handlers (fan-out 3, sequential)

    [Fact]
    public async Task Notification_ThreeHandlers_ShouldSustainThroughput()
    {
        if (!ShouldRun()) return;
        const int ops = 20_000;
        var (mediator, host) = await CreateHostAsync(svc =>
        {
            svc.AddSingleton<INotificationHandler<NotifFanout3>, NotifFanout3HandlerA>();
            svc.AddSingleton<INotificationHandler<NotifFanout3>, NotifFanout3HandlerB>();
            svc.AddSingleton<INotificationHandler<NotifFanout3>, NotifFanout3HandlerC>();
        });
        using var _ = host;
        var ct = TestContext.Current.CancellationToken;
        var tfm = Tfm;
        var sw = Stopwatch.GetTimestamp();
        for (var i = 0; i < ops; i++) await mediator.Notify(new NotifFanout3(i), ct);
        Emit("variant_notif_3h", tfm, ops, sw);
    }

    #endregion

    // =========================================================================
    // Infrastructure helpers
    // =========================================================================

    private static string Tfm => AppContext.TargetFrameworkName ?? "unknown";

    private void Emit(string scenario, string tfm, int ops, long startTimestamp)
    {
        var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
        var throughput = ops / elapsed.TotalSeconds;
        output.WriteLine(
            $"LOAD_RESULT {scenario} tfm={tfm} ops={ops} elapsed_ms={elapsed.TotalMilliseconds:F2} throughput_ops_s={throughput:F2}"
        );
        Assert.True(throughput > 500, $"Unexpected low throughput for {scenario}: {throughput:F2} ops/s");
    }

    private static Task RunParallelAsync(int ops, CancellationToken ct, Func<int, Task> body) =>
        Parallel.ForEachAsync(
            Enumerable.Range(0, ops),
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount), CancellationToken = ct },
            async (i, _) => await body(i)
        );

    private static async Task<(IMediator mediator, IHost host)> CreateHostAsync(
        Action<IServiceCollection> registerHandlers)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate();
        registerHandlers(builder.Services);
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return (host.Services.GetRequiredService<IMediator>(), host);
    }

    private static bool ShouldRun() =>
        string.Equals(Environment.GetEnvironmentVariable("NETMEDIATE_RUN_PERFORMANCE_TESTS"),
            "true", StringComparison.OrdinalIgnoreCase);

    // =========================================================================
    // Message types — each one unique so handlers never bleed across tests
    // =========================================================================

    // Section A – validation variants
    public sealed record CmdNoValid(int V) : ICommand;
    public sealed record CmdSelfValid(int V) : ICommand, IValidatable
    {
        public Task<ValidationResult> ValidateAsync() =>
            Task.FromResult(ValidationResult.Success!);
    }
    public sealed record CmdHandlerValid(int V) : ICommand;

    public sealed record NotifNoValid(int V) : INotification;
    public sealed record NotifSelfValid(int V) : INotification, IValidatable
    {
        public Task<ValidationResult> ValidateAsync() =>
            Task.FromResult(ValidationResult.Success!);
    }
    public sealed record NotifHandlerValid(int V) : INotification;

    // Section B – behavior variants
    public sealed record CmdNoBeh(int V) : ICommand;
    public sealed record CmdOneBeh(int V) : ICommand;
    public sealed record CmdTwoBeh(int V) : ICommand;

    public sealed record NotifNoBeh(int V) : INotification;
    public sealed record NotifOneBeh(int V) : INotification;
    public sealed record NotifTwoBeh(int V) : INotification;

    // Section C – fan-out variants
    public sealed record CmdFanout1(int V) : ICommand;
    public sealed record CmdFanout2(int V) : ICommand;
    public sealed record CmdFanout3(int V) : ICommand;

    public sealed record NotifFanout1(int V) : INotification;
    public sealed record NotifFanout2(int V) : INotification;
    public sealed record NotifFanout3(int V) : INotification;

    // =========================================================================
    // Handlers
    // =========================================================================

    // Section A
    private sealed class CmdNoValidHandler : ICommandHandler<CmdNoValid>
    { public Task Handle(CmdNoValid m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class CmdSelfValidHandler : ICommandHandler<CmdSelfValid>
    { public Task Handle(CmdSelfValid m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class CmdHandlerValidHandler : ICommandHandler<CmdHandlerValid>
    { public Task Handle(CmdHandlerValid m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class CmdHandlerValidValidator : IValidationHandler<CmdHandlerValid>
    {
        public Task<ValidationResult> ValidateAsync(CmdHandlerValid m, CancellationToken ct = default) =>
            Task.FromResult(ValidationResult.Success!);
    }

    private sealed class NotifNoValidHandler : INotificationHandler<NotifNoValid>
    { public Task Handle(NotifNoValid m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifSelfValidHandler : INotificationHandler<NotifSelfValid>
    { public Task Handle(NotifSelfValid m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifHandlerValidHandler : INotificationHandler<NotifHandlerValid>
    { public Task Handle(NotifHandlerValid m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifHandlerValidValidator : IValidationHandler<NotifHandlerValid>
    {
        public Task<ValidationResult> ValidateAsync(NotifHandlerValid m, CancellationToken ct = default) =>
            Task.FromResult(ValidationResult.Success!);
    }

    // Section B
    private sealed class CmdNoBehHandler : ICommandHandler<CmdNoBeh>
    { public Task Handle(CmdNoBeh m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class CmdOneBehHandler : ICommandHandler<CmdOneBeh>
    { public Task Handle(CmdOneBeh m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class CmdTwoBehHandler : ICommandHandler<CmdTwoBeh>
    { public Task Handle(CmdTwoBeh m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifNoBehHandler : INotificationHandler<NotifNoBeh>
    { public Task Handle(NotifNoBeh m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifOneBehHandler : INotificationHandler<NotifOneBeh>
    { public Task Handle(NotifOneBeh m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifTwoBehHandler : INotificationHandler<NotifTwoBeh>
    { public Task Handle(NotifTwoBeh m, CancellationToken ct = default) => Task.CompletedTask; }

    // Section C
    private sealed class CmdFanout1HandlerA : ICommandHandler<CmdFanout1>
    { public Task Handle(CmdFanout1 m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class CmdFanout2HandlerA : ICommandHandler<CmdFanout2>
    { public Task Handle(CmdFanout2 m, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class CmdFanout2HandlerB : ICommandHandler<CmdFanout2>
    { public Task Handle(CmdFanout2 m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class CmdFanout3HandlerA : ICommandHandler<CmdFanout3>
    { public Task Handle(CmdFanout3 m, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class CmdFanout3HandlerB : ICommandHandler<CmdFanout3>
    { public Task Handle(CmdFanout3 m, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class CmdFanout3HandlerC : ICommandHandler<CmdFanout3>
    { public Task Handle(CmdFanout3 m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifFanout1HandlerA : INotificationHandler<NotifFanout1>
    { public Task Handle(NotifFanout1 m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifFanout2HandlerA : INotificationHandler<NotifFanout2>
    { public Task Handle(NotifFanout2 m, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class NotifFanout2HandlerB : INotificationHandler<NotifFanout2>
    { public Task Handle(NotifFanout2 m, CancellationToken ct = default) => Task.CompletedTask; }

    private sealed class NotifFanout3HandlerA : INotificationHandler<NotifFanout3>
    { public Task Handle(NotifFanout3 m, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class NotifFanout3HandlerB : INotificationHandler<NotifFanout3>
    { public Task Handle(NotifFanout3 m, CancellationToken ct = default) => Task.CompletedTask; }
    private sealed class NotifFanout3HandlerC : INotificationHandler<NotifFanout3>
    { public Task Handle(NotifFanout3 m, CancellationToken ct = default) => Task.CompletedTask; }

    // =========================================================================
    // No-op behaviors — pass-through wrappers for overhead measurement
    // =========================================================================

    private sealed class NoOpCommandBehavior<TMessage> : IPipelineBehavior<TMessage, Task>
        where TMessage : notnull, ICommand
    {
        public Task Handle(TMessage msg, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken ct = default) =>
            next(msg, ct);
    }

    private sealed class NoOpCommandBehavior2<TMessage> : IPipelineBehavior<TMessage, Task>
        where TMessage : notnull, ICommand
    {
        public Task Handle(TMessage msg, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken ct = default) =>
            next(msg, ct);
    }

    private sealed class NoOpNotificationBehavior<TMessage> : IPipelineBehavior<TMessage, Task>
        where TMessage : notnull, INotification
    {
        public Task Handle(TMessage msg, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken ct = default) =>
            next(msg, ct);
    }

    private sealed class NoOpNotificationBehavior2<TMessage> : IPipelineBehavior<TMessage, Task>
        where TMessage : notnull, INotification
    {
        public Task Handle(TMessage msg, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken ct = default) =>
            next(msg, ct);
    }
}
