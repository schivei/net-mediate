using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Resilience.Tests;

/// <summary>
/// Targets coverage gaps in NetMediate core paths that the main resilience tests do not exercise:
/// command Send, stream RequestStream, Notify(IEnumerable), RegisterNotificationBehavior,
/// and RegisterCommandHandler/RegisterStreamHandler (type-based).
/// </summary>
public sealed class ResilienceCoverageTests
{
    // ── Command Send ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_WithCommandHandlerAndBehavior_ShouldInvokeHandlerAndBehavior()
    {
        CmdTrace.Reset();

        using var host = await CreateHostWithCommandAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();

        await mediator.Send(new CmdMessage("hello"), TestContext.Current.CancellationToken);

        Assert.True(CmdTrace.BehaviorCalled);
        Assert.True(CmdTrace.HandlerCalled);
    }

    [Fact]
    public async Task Send_WhenCommandHandlerThrows_ShouldWrapInMediatorException()
    {
        using var host = await CreateHostWithThrowingCommandAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<MediatorException>(
            () => mediator.Send(new ThrowingCmdMessage("fail"), TestContext.Current.CancellationToken)
        );

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(typeof(ThrowingCmdMessage), ex.MessageType);
    }

    [Fact]
    public async Task Send_Enumerable_WithCommandHandler_ShouldInvokeHandlerForEachMessage()
    {
        MultiCmdTrace.Reset();

        using var host = await CreateHostWithMultiCommandAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();

        IEnumerable<MultiCmdMessage> messages = [new("a"), new("b"), new("c")];
        await mediator.Send(messages, TestContext.Current.CancellationToken);

        Assert.Equal(3, MultiCmdTrace.Count);
    }

    // ── Stream RequestStream ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestStream_WithStreamHandlerAndBehavior_ShouldYieldItems()
    {
        using var host = await CreateHostWithStreamAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();

        var results = new List<int>();
        await foreach (var item in mediator.RequestStream<StreamMsg, int>(
            new StreamMsg(3), TestContext.Current.CancellationToken))
        {
            results.Add(item);
        }

        Assert.Equal([1, 2, 3], results);
    }

    // ── Notify(IEnumerable) ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Notify_Enumerable_ShouldDispatchEachMessage()
    {
        EnumNotifyTrace.Reset();

        using var host = await CreateHostWithEnumNotifyAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();

        IEnumerable<EnumNotifyMsg> messages = [new("x"), new("y")];
        await mediator.Notify(messages, TestContext.Current.CancellationToken);

        // Allow fire-and-forget dispatch to complete
        await WaitForAsync(() => EnumNotifyTrace.Count >= 2, TestContext.Current.CancellationToken);

        Assert.Equal(2, EnumNotifyTrace.Count);
    }

    // ── RegisterNotificationBehavior ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterNotificationBehavior_ShouldWrapDispatch()
    {
        NotifBehaviorTrace.Reset();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterNotificationHandler<NotifBehaviorHandler, NotifBehaviorMsg>();
            reg.RegisterNotificationBehavior<NotifBehaviorImpl, NotifBehaviorMsg>();
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Notify(new NotifBehaviorMsg("test"), TestContext.Current.CancellationToken);

        await WaitForAsync(() => NotifBehaviorTrace.Count >= 2, TestContext.Current.CancellationToken);

        Assert.Contains("behavior:pre", NotifBehaviorTrace.Entries);
        Assert.Contains("handler", NotifBehaviorTrace.Entries);
    }

    // ── MediatorException null handler-type branch ────────────────────────────────────────────

    [Fact]
    public void MediatorException_WithNullHandlerType_BuildsMessageWithoutHandlerName()
    {
        var inner = new Exception("fail");
        var ex = new MediatorException(typeof(CmdMessage), null, null, inner);

        Assert.Null(ex.HandlerType);
        Assert.Contains("CmdMessage", ex.Message);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task WaitForAsync(Func<bool> predicate, CancellationToken ct)
    {
        for (var i = 0; i < 200; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (predicate()) return;
            await Task.Delay(10, ct);
        }
        Assert.Fail("Timed out waiting for condition.");
    }

    private static async Task<IHost> CreateHostWithCommandAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterCommandHandler<CmdHandler, CmdMessage>();
            reg.RegisterBehavior<CmdBehavior, CmdMessage, Task>();
        });
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<IHost> CreateHostWithThrowingCommandAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterCommandHandler<ThrowingCmdHandler, ThrowingCmdMessage>();
        });
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<IHost> CreateHostWithMultiCommandAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterCommandHandler<MultiCmdHandler, MultiCmdMessage>();
        });
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<IHost> CreateHostWithStreamAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterStreamHandler<StreamHandler, StreamMsg, int>();
            reg.RegisterBehavior<StreamBehavior, StreamMsg, IAsyncEnumerable<int>>();
        });
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private static async Task<IHost> CreateHostWithEnumNotifyAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterNotificationHandler<EnumNotifyHandler, EnumNotifyMsg>();
        });
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    // ── Message types ────────────────────────────────────────────────────────────────────────

    public sealed record CmdMessage(string Value);
    public sealed record ThrowingCmdMessage(string Value);
    public sealed record MultiCmdMessage(string Value);
    public sealed record StreamMsg(int Count);
    public sealed record EnumNotifyMsg(string Value);
    public sealed record NotifBehaviorMsg(string Value);

    // ── Trace helpers ─────────────────────────────────────────────────────────────────────────

    private static class CmdTrace
    {
        private static volatile bool _behaviorCalled;
        private static volatile bool _handlerCalled;
        public static bool BehaviorCalled => _behaviorCalled;
        public static bool HandlerCalled => _handlerCalled;
        public static void SetBehavior() => _behaviorCalled = true;
        public static void SetHandler() => _handlerCalled = true;
        public static void Reset() { _behaviorCalled = false; _handlerCalled = false; }
    }

    private static class MultiCmdTrace
    {
        private static int _count;
        public static int Count => Volatile.Read(ref _count);
        public static void Increment() => Interlocked.Increment(ref _count);
        public static void Reset() => Interlocked.Exchange(ref _count, 0);
    }

    private static class EnumNotifyTrace
    {
        private static int _count;
        public static int Count => Volatile.Read(ref _count);
        public static void Increment() => Interlocked.Increment(ref _count);
        public static void Reset() => Interlocked.Exchange(ref _count, 0);
    }

    private static class NotifBehaviorTrace
    {
        private static readonly System.Collections.Concurrent.ConcurrentBag<string> _entries = [];
        public static IReadOnlyCollection<string> Entries => [.. _entries];
        public static int Count => _entries.Count;
        public static void Add(string entry) => _entries.Add(entry);
        public static void Reset() { while (_entries.TryTake(out _)) { } }
    }

    // ── Handlers ────────────────────────────────────────────────────────────────────────────

    private sealed class CmdHandler : ICommandHandler<CmdMessage>
    {
        public Task Handle(CmdMessage command, CancellationToken ct = default)
        {
            CmdTrace.SetHandler();
            return Task.CompletedTask;
        }
    }

    private sealed class CmdBehavior : IPipelineBehavior<CmdMessage, Task>
    {
        public async Task Handle(CmdMessage message, PipelineBehaviorDelegate<CmdMessage, Task> next, CancellationToken ct = default)
        {
            CmdTrace.SetBehavior();
            await next(message, ct);
        }
    }

    private sealed class ThrowingCmdHandler : ICommandHandler<ThrowingCmdMessage>
    {
        public Task Handle(ThrowingCmdMessage command, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("command failure"));
    }

    private sealed class MultiCmdHandler : ICommandHandler<MultiCmdMessage>
    {
        public Task Handle(MultiCmdMessage command, CancellationToken ct = default)
        {
            MultiCmdTrace.Increment();
            return Task.CompletedTask;
        }
    }

    private sealed class StreamHandler : IStreamHandler<StreamMsg, int>
    {
        public async IAsyncEnumerable<int> Handle(
            StreamMsg query,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            for (var i = 1; i <= query.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                yield return i;
                await Task.Yield();
            }
        }
    }

    private sealed class StreamBehavior : IPipelineStreamBehavior<StreamMsg, int>
    {
        public IAsyncEnumerable<int> Handle(
            StreamMsg message,
            PipelineBehaviorDelegate<StreamMsg, IAsyncEnumerable<int>> next,
            CancellationToken ct = default) => next(message, ct);
    }

    private sealed class EnumNotifyHandler : INotificationHandler<EnumNotifyMsg>
    {
        public Task Handle(EnumNotifyMsg notification, CancellationToken ct = default)
        {
            EnumNotifyTrace.Increment();
            return Task.CompletedTask;
        }
    }

    private sealed class NotifBehaviorHandler : INotificationHandler<NotifBehaviorMsg>
    {
        public Task Handle(NotifBehaviorMsg notification, CancellationToken ct = default)
        {
            NotifBehaviorTrace.Add("handler");
            return Task.CompletedTask;
        }
    }

    private sealed class NotifBehaviorImpl : IPipelineNotificationBehavior<NotifBehaviorMsg>
    {
        public async Task Handle(
            NotifBehaviorMsg message,
            PipelineBehaviorDelegate<NotifBehaviorMsg, Task> next,
            CancellationToken ct = default)
        {
            NotifBehaviorTrace.Add("behavior:pre");
            await next(message, ct);
        }
    }
}
