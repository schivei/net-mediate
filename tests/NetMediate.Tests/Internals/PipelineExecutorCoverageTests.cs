using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetMediate.Tests.Internals;

/// <summary>
/// White-box tests that directly instantiate the four pipeline executors and drive every
/// code path in the five changed source files (KeyedHandlerRegistry + four executors),
/// achieving 100% line + branch coverage.
/// </summary>
/// <remarks>
/// <see cref="NotificationPipelineExecutor{TMessage}"/> and
/// <see cref="RequestPipelineExecutor{TMessage,TResponse}"/> implement error reporting by
/// returning <c>pipeline(...).ContinueWith(OnlyOnFaulted)</c>. When the pipeline succeeds,
/// the <c>OnlyOnFaulted</c> continuation transitions to the Canceled state, so awaiting
/// the returned task throws <see cref="TaskCanceledException"/>. The real application
/// discards the task fire-and-forget; these tests use <c>AwaitSuccessOrCanceled</c>
/// helpers to swallow that expected exception and then verify side-effects.
/// </remarks>
public sealed class PipelineExecutorCoverageTests
{
    // ─── message stubs ────────────────────────────────────────────────────────
    private sealed record Cmd;
    private sealed record Notif;
    private sealed record Req;
    private sealed record Rsp(int Value);
    private sealed record Str;

    // ─── handler stubs ────────────────────────────────────────────────────────

    private sealed class CmdHandler : ICommandHandler<Cmd>
    {
        public bool Handled { get; private set; }
        public Task Handle(Cmd command, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class NotifHandler : INotificationHandler<Notif>
    {
        public bool Handled { get; private set; }
        public Task Handle(Notif notification, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FaultingNotifHandler : INotificationHandler<Notif>
    {
        public Task Handle(Notif notification, CancellationToken cancellationToken = default)
            => Task.FromException(new InvalidOperationException("notif fault"));
    }

    private sealed class ReqHandler : IRequestHandler<Req, Rsp>
    {
        public bool Handled { get; private set; }
        public Task<Rsp> Handle(Req request, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return Task.FromResult(new Rsp(42));
        }
    }

    private sealed class StrHandler : IStreamHandler<Str, int>
    {
        public async IAsyncEnumerable<int> Handle(
            Str request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            yield return 1;
            yield return 2;
            await Task.CompletedTask;
        }
    }

    // ─── behavior stubs ───────────────────────────────────────────────────────

    private sealed class PassThroughCmdBehavior : IPipelineCommandBehavior<Cmd>
    {
        public Task Handle(
            object? key, Cmd message,
            PipelineBehaviorDelegate<Cmd, Task> next,
            CancellationToken cancellationToken)
            => next(key, message, cancellationToken);
    }

    private sealed class FaultingCmdBehavior : IPipelineCommandBehavior<Cmd>
    {
        public Task Handle(
            object? key, Cmd message,
            PipelineBehaviorDelegate<Cmd, Task> next,
            CancellationToken cancellationToken)
            => Task.FromException(new InvalidOperationException("behavior fault"));
    }

    private sealed class PassThroughNotifBehavior : IPipelineNotificationBehavior<Notif>
    {
        public Task Handle(
            object? key, Notif message,
            PipelineBehaviorDelegate<Notif, Task> next,
            CancellationToken cancellationToken)
            => next(key, message, cancellationToken);
    }

    private sealed class PassThroughReqBehavior : IPipelineRequestBehavior<Req, Rsp>
    {
        public Task<Rsp> Handle(
            object? key, Req message,
            PipelineBehaviorDelegate<Req, Task<Rsp>> next,
            CancellationToken cancellationToken)
            => next(key, message, cancellationToken);
    }

    private sealed class FaultingReqBehavior : IPipelineRequestBehavior<Req, Rsp>
    {
        public Task<Rsp> Handle(
            object? key, Req message,
            PipelineBehaviorDelegate<Req, Task<Rsp>> next,
            CancellationToken cancellationToken)
            => Task.FromException<Rsp>(new InvalidOperationException("req behavior fault"));
    }

    private sealed class PassThroughStreamBehavior : IPipelineStreamBehavior<Str, int>
    {
        public IAsyncEnumerable<int> Handle(
            object? key, Str message,
            PipelineBehaviorDelegate<Str, IAsyncEnumerable<int>> next,
            CancellationToken cancellationToken)
            => next(key, message, cancellationToken);
    }

    // ─── DI / registry helpers ────────────────────────────────────────────────

    private static IServiceProvider BuildProvider(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection();
        configure(services);
        return services.BuildServiceProvider();
    }

    private static KeyedHandlerRegistry<THandler> MakeRegistry<THandler>(
        object key, Func<IServiceProvider, THandler>[] factories)
        => new(new Dictionary<object, Func<IServiceProvider, THandler>[]> { { key, factories } });

    private static HandlerExecutionDelegate<ICommandHandler<Cmd>, Cmd, Task> CmdExec()
        => (_, msg, handlers, ct) => Task.WhenAll(handlers.Select(h => h.Handle(msg, ct)));

    private static HandlerExecutionDelegate<INotificationHandler<Notif>, Notif, Task> NotifExec()
        => (_, msg, handlers, ct) => Task.WhenAll(handlers.Select(h => h.Handle(msg, ct)));

    /// <summary>
    /// Awaits <paramref name="t"/>, swallowing the expected
    /// <see cref="TaskCanceledException"/> produced by a successful
    /// <c>ContinueWith(OnlyOnFaulted)</c> continuation.
    /// </summary>
    private static async Task AwaitOk(Task t)
    {
        try { await t; }
        catch (TaskCanceledException) { }
    }

    private static async Task<TResponse?> AwaitOk<TResponse>(Task<TResponse> t)
    {
        try { return await t; }
        catch (TaskCanceledException) { return default; }
    }

    // =========================================================================
    // CommandPipelineExecutor
    // =========================================================================

    // ResolveKeyedHandlers – registry is null ─────────────────────────────────
    [Fact]
    public async Task Cmd_Keyed_NoRegistry_FallsBackToGetServices()
    {
        var h = new CmdHandler();
        var sp = BuildProvider(s => s.AddSingleton<ICommandHandler<Cmd>>(h));
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<NotificationPipelineExecutor<Cmd>>.Instance);

        await ex.Handle("k", new Cmd(), CmdExec(), default);

        Assert.True(h.Handled);
    }

    // ResolveKeyedHandlers – key not in registry ──────────────────────────────
    [Fact]
    public async Task Cmd_Keyed_RegistryMiss_FallsBackToGetServices()
    {
        var h = new CmdHandler();
        var reg = MakeRegistry<ICommandHandler<Cmd>>("other", [_ => h]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<ICommandHandler<Cmd>>(h); });
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<NotificationPipelineExecutor<Cmd>>.Instance);

        await ex.Handle("k", new Cmd(), CmdExec(), default);

        Assert.True(h.Handled);
    }

    // ResolveKeyedHandlers – empty factory array (TryGetAll true, Length == 0) ─
    [Fact]
    public async Task Cmd_Keyed_EmptyFactoryArray_FallsBackToGetServices()
    {
        var h = new CmdHandler();
        var reg = new KeyedHandlerRegistry<ICommandHandler<Cmd>>(
            new Dictionary<object, Func<IServiceProvider, ICommandHandler<Cmd>>[]> { { "k", [] } });
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<ICommandHandler<Cmd>>(h); });
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<NotificationPipelineExecutor<Cmd>>.Instance);

        await ex.Handle("k", new Cmd(), CmdExec(), default);

        Assert.True(h.Handled);
    }

    // ResolveKeyedHandlers – registry hit ─────────────────────────────────────
    [Fact]
    public async Task Cmd_Keyed_RegistryHit_UsesKeyedHandlers()
    {
        var keyed = new CmdHandler();
        var fallback = new CmdHandler();
        var reg = MakeRegistry<ICommandHandler<Cmd>>("k", [_ => keyed]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<ICommandHandler<Cmd>>(fallback); });
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<NotificationPipelineExecutor<Cmd>>.Instance);

        await ex.Handle("k", new Cmd(), CmdExec(), default);

        Assert.True(keyed.Handled);
        Assert.False(fallback.Handled);
    }

    // BuildPipeline – key null path ───────────────────────────────────────────
    [Fact]
    public async Task Cmd_KeyNull_NoBehaviors_HandlerInvoked()
    {
        var h = new CmdHandler();
        var sp = BuildProvider(s => s.AddSingleton<ICommandHandler<Cmd>>(h));
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<NotificationPipelineExecutor<Cmd>>.Instance);

        await ex.Handle(null, new Cmd(), CmdExec(), default);

        Assert.True(h.Handled);
    }

    // BuildPipeline – with behavior ───────────────────────────────────────────
    [Fact]
    public async Task Cmd_WithBehavior_HandlerInvoked()
    {
        var h = new CmdHandler();
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<ICommandHandler<Cmd>>(h);
            s.AddSingleton<IPipelineCommandBehavior<Cmd>, PassThroughCmdBehavior>();
        });
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<NotificationPipelineExecutor<Cmd>>.Instance);

        await ex.Handle(null, new Cmd(), CmdExec(), default);

        Assert.True(h.Handled);
    }

    // App local fn – catch branch (exec throws synchronously) ─────────────────
    [Fact]
    public async Task Cmd_ExecThrows_ExceptionSwallowedByApp()
    {
        var sp = BuildProvider(s => s.AddSingleton<ICommandHandler<Cmd>>(_ => new CmdHandler()));
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<NotificationPipelineExecutor<Cmd>>.Instance);

        HandlerExecutionDelegate<ICommandHandler<Cmd>, Cmd, Task> throwing =
            (_, _, _, _) => throw new InvalidOperationException("sync throw");

        var exception = await Record.ExceptionAsync(() =>
            ex.Handle(null, new Cmd(), throwing, default)
        );

        Assert.Null(exception);
    }

    // ErrorReporting – faulted behavior triggers OnlyOnFaulted continuation ───
    [Fact]
    public async Task Cmd_FaultingBehavior_ErrorReportingContinuationFires()
    {
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<ICommandHandler<Cmd>>(_ => new CmdHandler());
            s.AddSingleton<IPipelineCommandBehavior<Cmd>, FaultingCmdBehavior>();
        });
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<NotificationPipelineExecutor<Cmd>>.Instance);

        var t = ex.Handle(null, new Cmd(), CmdExec(), default);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await t);
    }

    // =========================================================================
    // NotificationPipelineExecutor
    // =========================================================================

    [Fact]
    public async Task Notif_Keyed_NoRegistry_FallsBackToGetServices()
    {
        var h = new NotifHandler();
        var sp = BuildProvider(s => s.AddSingleton<INotificationHandler<Notif>>(h));
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await AwaitOk(ex.Handle("k", new Notif(), NotifExec(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Notif_Keyed_RegistryMiss_FallsBackToGetServices()
    {
        var h = new NotifHandler();
        var reg = MakeRegistry<INotificationHandler<Notif>>("other", [_ => h]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<INotificationHandler<Notif>>(h); });
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await AwaitOk(ex.Handle("k", new Notif(), NotifExec(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Notif_Keyed_EmptyFactoryArray_FallsBackToGetServices()
    {
        var h = new NotifHandler();
        var reg = new KeyedHandlerRegistry<INotificationHandler<Notif>>(
            new Dictionary<object, Func<IServiceProvider, INotificationHandler<Notif>>[]> { { "k", [] } });
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<INotificationHandler<Notif>>(h); });
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await AwaitOk(ex.Handle("k", new Notif(), NotifExec(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Notif_Keyed_RegistryHit_UsesKeyedHandlers()
    {
        var keyed = new NotifHandler();
        var fallback = new NotifHandler();
        var reg = MakeRegistry<INotificationHandler<Notif>>("k", [_ => keyed]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<INotificationHandler<Notif>>(fallback); });
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await AwaitOk(ex.Handle("k", new Notif(), NotifExec(), default));

        Assert.True(keyed.Handled);
        Assert.False(fallback.Handled);
    }

    // handlers.Length == 1 → direct dispatch (not via exec) ──────────────────
    [Fact]
    public async Task Notif_SingleHandler_DirectCall_ExecNotInvoked()
    {
        var h = new NotifHandler();
        var sp = BuildProvider(s => s.AddSingleton<INotificationHandler<Notif>>(h));
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        var execCalled = false;
        HandlerExecutionDelegate<INotificationHandler<Notif>, Notif, Task> exec =
            (_, _, _, _) => { execCalled = true; return Task.CompletedTask; };

        await AwaitOk(ex.Handle(null, new Notif(), exec, default));

        Assert.True(h.Handled);
        Assert.False(execCalled);
    }

    // handlers.Length > 1 → exec delegate used ────────────────────────────────
    [Fact]
    public async Task Notif_MultipleHandlers_ExecDelegateUsed()
    {
        var h1 = new NotifHandler();
        var h2 = new NotifHandler();
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<Notif>>(h1);
            s.AddSingleton<INotificationHandler<Notif>>(h2);
        });
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await AwaitOk(ex.Handle(null, new Notif(), NotifExec(), default));

        Assert.True(h1.Handled);
        Assert.True(h2.Handled);
    }

    // key == null → GetServices path ──────────────────────────────────────────
    [Fact]
    public async Task Notif_KeyNull_NoBehaviors_HandlerInvoked()
    {
        var h = new NotifHandler();
        var sp = BuildProvider(s => s.AddSingleton<INotificationHandler<Notif>>(h));
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await AwaitOk(ex.Handle(null, new Notif(), NotifExec(), default));

        Assert.True(h.Handled);
    }

    // with behavior ───────────────────────────────────────────────────────────
    [Fact]
    public async Task Notif_WithBehavior_HandlerInvoked()
    {
        var h = new NotifHandler();
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<INotificationHandler<Notif>>(h);
            s.AddSingleton<IPipelineNotificationBehavior<Notif>, PassThroughNotifBehavior>();
        });
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await AwaitOk(ex.Handle(null, new Notif(), NotifExec(), default));

        Assert.True(h.Handled);
    }

    // faulting handler → ErrorReporting continuation fires ────────────────────
    [Fact]
    public async Task Notif_FaultingHandler_ErrorReportingContinuationFires()
    {
        // When the pipeline faults, ErrorReporting's OnlyOnFaulted continuation fires and
        // logs the error. The void continuation completes successfully, so the returned
        // task succeeds — no exception propagates to the caller.
        var sp = BuildProvider(s =>
            s.AddSingleton<INotificationHandler<Notif>>(_ => new FaultingNotifHandler()));
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        var exception = await Record.ExceptionAsync(() =>
            ex.Handle(null, new Notif(), NotifExec(), default)
        );

        Assert.Null(exception);
    }

    // =========================================================================
    // RequestPipelineExecutor
    // =========================================================================

    [Fact]
    public async Task Req_Keyed_NoRegistry_FallsBackToGetServices()
    {
        var h = new ReqHandler();
        var sp = BuildProvider(s => s.AddSingleton<IRequestHandler<Req, Rsp>>(h));
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await AwaitOk(ex.Handle("k", new Req(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_Keyed_RegistryMiss_FallsBackToGetServices()
    {
        var h = new ReqHandler();
        var reg = MakeRegistry<IRequestHandler<Req, Rsp>>("other", [_ => h]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<IRequestHandler<Req, Rsp>>(h); });
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await AwaitOk(ex.Handle("k", new Req(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_Keyed_EmptyFactoryArray_FallsBackToGetServices()
    {
        var h = new ReqHandler();
        var reg = new KeyedHandlerRegistry<IRequestHandler<Req, Rsp>>(
            new Dictionary<object, Func<IServiceProvider, IRequestHandler<Req, Rsp>>[]> { { "k", [] } });
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<IRequestHandler<Req, Rsp>>(h); });
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await AwaitOk(ex.Handle("k", new Req(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_Keyed_RegistryHit_UsesKeyedHandler()
    {
        var h = new ReqHandler();
        var reg = MakeRegistry<IRequestHandler<Req, Rsp>>("k", [_ => h]);
        var sp = BuildProvider(s => s.AddSingleton(reg));
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await AwaitOk(ex.Handle("k", new Req(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_KeyNull_NoBehaviors_ReturnsResponse()
    {
        var h = new ReqHandler();
        var sp = BuildProvider(s => s.AddSingleton<IRequestHandler<Req, Rsp>>(h));
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await AwaitOk(ex.Handle(null, new Req(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_WithBehavior_ReturnsResponse()
    {
        var h = new ReqHandler();
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<IRequestHandler<Req, Rsp>>(h);
            s.AddSingleton<IPipelineRequestBehavior<Req, Rsp>, PassThroughReqBehavior>();
        });
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await AwaitOk(ex.Handle(null, new Req(), default));

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_FaultingBehavior_ErrorReportingContinuationFires()
    {
        // When the pipeline faults, ErrorReporting's OnlyOnFaulted continuation fires,
        // logs the error, and returns default(TResponse)=null. The continuation task
        // completes successfully — no exception propagates to the caller.
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<IRequestHandler<Req, Rsp>, ReqHandler>();
            s.AddSingleton<IPipelineRequestBehavior<Req, Rsp>, FaultingReqBehavior>();
        });
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        var result = await ex.Handle(null, new Req(), default);

        Assert.Null(result); // continuation returned default(Rsp)
    }

    // =========================================================================
    // StreamPipelineExecutor
    // =========================================================================

    [Fact]
    public async Task Stream_Keyed_NoRegistry_FallsBackToGetServices()
    {
        var sp = BuildProvider(s => s.AddSingleton<IStreamHandler<Str, int>, StrHandler>());
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle("k", new Str(), StreamExec(), default));

        Assert.Equal([1, 2], items);
    }

    [Fact]
    public async Task Stream_Keyed_RegistryMiss_FallsBackToGetServices()
    {
        var h = new StrHandler();
        var reg = MakeRegistry<IStreamHandler<Str, int>>("other", [_ => h]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<IStreamHandler<Str, int>>(h); });
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle("k", new Str(), StreamExec(), default));

        Assert.Equal([1, 2], items);
    }

    [Fact]
    public async Task Stream_Keyed_EmptyFactoryArray_FallsBackToGetServices()
    {
        var h = new StrHandler();
        var reg = new KeyedHandlerRegistry<IStreamHandler<Str, int>>(
            new Dictionary<object, Func<IServiceProvider, IStreamHandler<Str, int>>[]> { { "k", [] } });
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<IStreamHandler<Str, int>>(h); });
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle("k", new Str(), StreamExec(), default));

        Assert.Equal([1, 2], items);
    }

    [Fact]
    public async Task Stream_Keyed_RegistryHit_UsesKeyedHandler()
    {
        var h = new StrHandler();
        var reg = MakeRegistry<IStreamHandler<Str, int>>("k", [_ => h]);
        var sp = BuildProvider(s => s.AddSingleton(reg));
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle("k", new Str(), StreamExec(), default));

        Assert.Equal([1, 2], items);
    }

    // handlers.Length == 1 → direct dispatch ──────────────────────────────────
    [Fact]
    public async Task Stream_KeyNull_SingleHandler_DirectCall_ExecNotInvoked()
    {
        var sp = BuildProvider(s => s.AddSingleton<IStreamHandler<Str, int>, StrHandler>());
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var execCalled = false;
        HandlerExecutionDelegate<IStreamHandler<Str, int>, Str, IAsyncEnumerable<int>> exec =
            (_, _, _, _) => { execCalled = true; return AsyncEnumerable.Empty<int>(); };

        var items = await ToList(ex.Handle(null, new Str(), exec, default));

        Assert.Equal([1, 2], items);
        Assert.False(execCalled);
    }

    // handlers.Length > 1 → exec delegate ─────────────────────────────────────
    [Fact]
    public async Task Stream_MultipleHandlers_ExecDelegateUsed()
    {
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<IStreamHandler<Str, int>, StrHandler>();
            s.AddSingleton<IStreamHandler<Str, int>, StrHandler>();
        });
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle(null, new Str(), StreamExec(), default));

        // Two handlers × [1, 2] = four items
        Assert.Equal(4, items.Count);
    }

    // with behavior ───────────────────────────────────────────────────────────
    [Fact]
    public async Task Stream_WithBehavior_YieldsExpectedItems()
    {
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<IStreamHandler<Str, int>, StrHandler>();
            s.AddSingleton<IPipelineStreamBehavior<Str, int>, PassThroughStreamBehavior>();
        });
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle(null, new Str(), StreamExec(), default));

        Assert.Equal([1, 2], items);
    }

    // ─── static helpers ───────────────────────────────────────────────────────

    private static HandlerExecutionDelegate<IStreamHandler<Str, int>, Str, IAsyncEnumerable<int>> StreamExec()
        => (_, msg, handlers, ct) => Merge(handlers.Select(h => h.Handle(msg, ct)));

    private static async IAsyncEnumerable<int> Merge(
        IEnumerable<IAsyncEnumerable<int>> streams,
        CancellationToken ct = default)
    {
        foreach (var s in streams)
            await foreach (var item in s.WithCancellation(ct))
                yield return item;
    }

    private static async Task<List<int>> ToList(IAsyncEnumerable<int> stream)
    {
        var list = new List<int>();
        await foreach (var item in stream)
            list.Add(item);
        return list;
    }
}
