using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace NetMediate.Tests.Internals;

/// <summary>
/// White-box tests that directly instantiate the four pipeline executors and drive every
/// code path in the five changed source files (KeyedHandlerRegistry + four executors),
/// achieving 100% line + branch coverage.
/// </summary>
public sealed class PipelineExecutorCoverageTests
{
    private static CancellationToken TestCancellationToken => global::Xunit.TestContext.Current.CancellationToken;

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

    // Async handler stubs – Task.Yield() ensures the task is NOT pre-completed
    // when returned, which forces ErrorReporting into the AwaitAndCatch path
    // and exercises the success-path closing braces (uncovered without these).

    private sealed class AsyncCmdHandler : ICommandHandler<Cmd>
    {
        public async Task Handle(Cmd command, CancellationToken cancellationToken = default)
            => await Task.Yield();
    }

    private sealed class AsyncNotifHandler : INotificationHandler<Notif>
    {
        public async Task Handle(Notif notification, CancellationToken cancellationToken = default)
            => await Task.Yield();
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

    private sealed class AsyncReqHandler : IRequestHandler<Req, Rsp>
    {
        public async Task<Rsp> Handle(Req request, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            return new Rsp(99);
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

    // =========================================================================
    // CommandPipelineExecutor
    // =========================================================================

    // ResolveKeyedHandlers – registry is null ─────────────────────────────────
    [Fact]
    public async Task Cmd_Keyed_NoRegistry_FallsBackToGetServices()
    {
        var h = new CmdHandler();
        var sp = BuildProvider(s => s.AddSingleton<ICommandHandler<Cmd>>(h));
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        await ex.Handle("k", new Cmd(), CmdExec(), TestCancellationToken);

        Assert.True(h.Handled);
    }

    // ResolveKeyedHandlers – key not in registry ──────────────────────────────
    [Fact]
    public async Task Cmd_Keyed_RegistryMiss_FallsBackToGetServices()
    {
        var h = new CmdHandler();
        var reg = MakeRegistry<ICommandHandler<Cmd>>("other", [_ => h]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<ICommandHandler<Cmd>>(h); });
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        await ex.Handle("k", new Cmd(), CmdExec(), TestCancellationToken);

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
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        await ex.Handle("k", new Cmd(), CmdExec(), TestCancellationToken);

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
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        await ex.Handle("k", new Cmd(), CmdExec(), TestCancellationToken);

        Assert.True(keyed.Handled);
        Assert.False(fallback.Handled);
    }

    // BuildPipeline – key null path ───────────────────────────────────────────
    [Fact]
    public async Task Cmd_KeyNull_NoBehaviors_HandlerInvoked()
    {
        var h = new CmdHandler();
        var sp = BuildProvider(s => s.AddSingleton<ICommandHandler<Cmd>>(h));
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        await ex.Handle(null, new Cmd(), CmdExec(), TestCancellationToken);

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
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        await ex.Handle(null, new Cmd(), CmdExec(), TestCancellationToken);

        Assert.True(h.Handled);
    }

    // App local fn – catch branch (exec throws synchronously) ─────────────────
    [Fact]
    public async Task Cmd_ExecThrows_ExceptionPropagates()
    {
        var sp = BuildProvider(s => s.AddSingleton<ICommandHandler<Cmd>>(_ => new CmdHandler()));
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        HandlerExecutionDelegate<ICommandHandler<Cmd>, Cmd, Task> throwing =
            (_, _, _, _) => throw new InvalidOperationException("sync throw");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ex.Handle(null, new Cmd(), throwing, TestCancellationToken)
        );
    }

    // ErrorReporting – faulted behavior propagates the pipeline exception ──────
    [Fact]
    public async Task Cmd_FaultingBehavior_ErrorReportingContinuationFires()
    {
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<ICommandHandler<Cmd>>(_ => new CmdHandler());
            s.AddSingleton<IPipelineCommandBehavior<Cmd>, FaultingCmdBehavior>();
        });
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        var t = ex.Handle(null, new Cmd(), CmdExec(), TestCancellationToken);
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await t);
    }

    // ErrorReporting – async handler completes successfully (covers AwaitAndCatch
    // success-path closing braces that are unreachable via pre-completed tasks) ─
    [Fact]
    public async Task Cmd_AsyncHandler_AwaitAndCatch_SuccessPath()
    {
        var sp = BuildProvider(s => s.AddSingleton<ICommandHandler<Cmd>>(_ => new AsyncCmdHandler()));
        var ex = new CommandPipelineExecutor<Cmd>(sp, NullLogger<CommandPipelineExecutor<Cmd>>.Instance);

        // AsyncCmdHandler uses Task.Yield(), so pipeline returns a non-completed
        // task → ErrorReporting calls AwaitAndCatch → task completes successfully.
        await ex.Handle(null, new Cmd(), CmdExec(), TestCancellationToken);
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

        await ex.Handle("k", new Notif(), NotifExec(), TestCancellationToken);

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Notif_Keyed_RegistryMiss_FallsBackToGetServices()
    {
        var h = new NotifHandler();
        var reg = MakeRegistry<INotificationHandler<Notif>>("other", [_ => h]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<INotificationHandler<Notif>>(h); });
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await ex.Handle("k", new Notif(), NotifExec(), TestCancellationToken);

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

        await ex.Handle("k", new Notif(), NotifExec(), TestCancellationToken);

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

        await ex.Handle("k", new Notif(), NotifExec(), TestCancellationToken);

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

        await ex.Handle(null, new Notif(), exec, TestCancellationToken);

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

        await ex.Handle(null, new Notif(), NotifExec(), TestCancellationToken);

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

        await ex.Handle(null, new Notif(), NotifExec(), TestCancellationToken);

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

        await ex.Handle(null, new Notif(), NotifExec(), TestCancellationToken);

        Assert.True(h.Handled);
    }

    // faulting handler → ErrorReporting continuation fires ────────────────────
    [Fact]
    public async Task Notif_FaultingHandler_ExceptionPropagates()
    {
        var sp = BuildProvider(s =>
            s.AddSingleton<INotificationHandler<Notif>>(_ => new FaultingNotifHandler()));
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ex.Handle(null, new Notif(), NotifExec(), TestCancellationToken)
        );
    }

    // ErrorReporting – async handler completes successfully (covers AwaitAndCatch
    // success-path closing braces that are unreachable via pre-completed tasks) ─
    [Fact]
    public async Task Notif_AsyncHandler_AwaitAndCatch_SuccessPath()
    {
        var sp = BuildProvider(s => s.AddSingleton<INotificationHandler<Notif>>(_ => new AsyncNotifHandler()));
        var ex = new NotificationPipelineExecutor<Notif>(sp, NullLogger<NotificationPipelineExecutor<Notif>>.Instance);

        // AsyncNotifHandler uses Task.Yield(), so pipeline returns a non-completed
        // task → ErrorReporting calls AwaitAndCatch → task completes successfully.
        await ex.Handle(null, new Notif(), NotifExec(), TestCancellationToken);
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

        await ex.Handle("k", new Req(), TestCancellationToken);

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_Keyed_RegistryMiss_FallsBackToGetServices()
    {
        var h = new ReqHandler();
        var reg = MakeRegistry<IRequestHandler<Req, Rsp>>("other", [_ => h]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<IRequestHandler<Req, Rsp>>(h); });
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await ex.Handle("k", new Req(), TestCancellationToken);

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

        await ex.Handle("k", new Req(), TestCancellationToken);

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_Keyed_RegistryHit_UsesKeyedHandler()
    {
        var h = new ReqHandler();
        var reg = MakeRegistry<IRequestHandler<Req, Rsp>>("k", [_ => h]);
        var sp = BuildProvider(s => s.AddSingleton(reg));
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await ex.Handle("k", new Req(), TestCancellationToken);

        Assert.True(h.Handled);
    }

    [Fact]
    public async Task Req_KeyNull_NoBehaviors_ReturnsResponse()
    {
        var h = new ReqHandler();
        var sp = BuildProvider(s => s.AddSingleton<IRequestHandler<Req, Rsp>>(h));
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        var response = await ex.Handle(null, new Req(), TestCancellationToken);

        Assert.True(h.Handled);
        Assert.Equal(42, response.Value);
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

        var response = await ex.Handle(null, new Req(), TestCancellationToken);

        Assert.True(h.Handled);
        Assert.Equal(42, response.Value);
    }

    [Fact]
    public async Task Req_FaultingBehavior_ExceptionPropagates()
    {
        var sp = BuildProvider(s =>
        {
            s.AddSingleton<IRequestHandler<Req, Rsp>, ReqHandler>();
            s.AddSingleton<IPipelineRequestBehavior<Req, Rsp>, FaultingReqBehavior>();
        });
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ex.Handle(null, new Req(), TestCancellationToken)
        );
    }

    // ErrorReporting – async handler completes successfully (covers AwaitAndCatch
    // success-path closing braces that are unreachable via pre-completed tasks) ─
    [Fact]
    public async Task Req_AsyncHandler_AwaitAndCatch_SuccessPath()
    {
        var sp = BuildProvider(s => s.AddSingleton<IRequestHandler<Req, Rsp>>(_ => new AsyncReqHandler()));
        var ex = new RequestPipelineExecutor<Req, Rsp>(sp, NullLogger<RequestPipelineExecutor<Req, Rsp>>.Instance);

        // AsyncReqHandler uses Task.Yield(), so pipeline returns a non-completed
        // task → ErrorReporting calls AwaitAndCatch → task completes successfully.
        var response = await ex.Handle(null, new Req(), TestCancellationToken);
        Assert.Equal(99, response.Value);
    }

    // =========================================================================
    // StreamPipelineExecutor
    // =========================================================================

    [Fact]
    public async Task Stream_Keyed_NoRegistry_FallsBackToGetServices()
    {
        var sp = BuildProvider(s => s.AddSingleton<IStreamHandler<Str, int>, StrHandler>());
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle("k", new Str(), StreamExec(), TestCancellationToken));

        Assert.Equal([1, 2], items);
    }

    [Fact]
    public async Task Stream_Keyed_RegistryMiss_FallsBackToGetServices()
    {
        var h = new StrHandler();
        var reg = MakeRegistry<IStreamHandler<Str, int>>("other", [_ => h]);
        var sp = BuildProvider(s => { s.AddSingleton(reg); s.AddSingleton<IStreamHandler<Str, int>>(h); });
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle("k", new Str(), StreamExec(), TestCancellationToken));

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

        var items = await ToList(ex.Handle("k", new Str(), StreamExec(), TestCancellationToken));

        Assert.Equal([1, 2], items);
    }

    [Fact]
    public async Task Stream_Keyed_RegistryHit_UsesKeyedHandler()
    {
        var h = new StrHandler();
        var reg = MakeRegistry<IStreamHandler<Str, int>>("k", [_ => h]);
        var sp = BuildProvider(s => s.AddSingleton(reg));
        var ex = new StreamPipelineExecutor<Str, int>(sp);

        var items = await ToList(ex.Handle("k", new Str(), StreamExec(), TestCancellationToken));

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

        var items = await ToList(ex.Handle(null, new Str(), exec, TestCancellationToken));

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

        var items = await ToList(ex.Handle(null, new Str(), StreamExec(), TestCancellationToken));

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

        var items = await ToList(ex.Handle(null, new Str(), StreamExec(), TestCancellationToken));

        Assert.Equal([1, 2], items);
    }

    // ─── static helpers ───────────────────────────────────────────────────────

    private static HandlerExecutionDelegate<IStreamHandler<Str, int>, Str, IAsyncEnumerable<int>> StreamExec()
        => (_, msg, handlers, ct) => Merge(handlers.Select(h => h.Handle(msg, ct)), ct);

    private static async IAsyncEnumerable<int> Merge(
        IEnumerable<IAsyncEnumerable<int>> streams,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
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
