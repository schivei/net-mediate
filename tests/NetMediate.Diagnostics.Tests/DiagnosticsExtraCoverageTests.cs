using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Diagnostics.Tests;

/// <summary>
/// Targets coverage gaps in the telemetry behavior error paths, MediatorException,
/// Notify(IEnumerable), and Send command paths that the primary telemetry test does not exercise.
/// </summary>
public sealed class DiagnosticsExtraCoverageTests
{
    // ── TelemetryNotificationBehavior — error path ────────────────────────────────────────────

    [Fact]
    public async Task TelemetryNotificationBehavior_WhenInnerBehaviorThrows_ShouldSetActivityErrorAndRethrow()
    {
        var activityErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NetMediateDiagnostics.ActivitySourceName,
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Status == ActivityStatusCode.Error)
                    activityErrors.Enqueue(activity.OperationName);
            },
        };
        ActivitySource.AddActivityListener(listener);

        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterNotificationHandler<NoopNotificationHandler, ErrorNotificationMsg>();
                // TelemetryNotificationBehavior registered first → becomes outermost
                reg.RegisterBehavior<TelemetryNotificationBehavior<ErrorNotificationMsg>, ErrorNotificationMsg, Task>();
                // ThrowingBehavior registered second → becomes innermost; throws before handler is reached
                reg.RegisterBehavior<ThrowingNotificationBehavior<ErrorNotificationMsg>, ErrorNotificationMsg, Task>();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => mediator.Notify(new ErrorNotificationMsg(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("NetMediate.Notify", activityErrors);
    }

    // ── TelemetryRequestBehavior — error path ─────────────────────────────────────────────────

    [Fact]
    public async Task TelemetryRequestBehavior_WhenHandlerThrows_ShouldSetActivityErrorAndRethrow()
    {
        var activityErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NetMediateDiagnostics.ActivitySourceName,
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Status == ActivityStatusCode.Error)
                    activityErrors.Enqueue(activity.OperationName);
            },
        };
        ActivitySource.AddActivityListener(listener);

        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterRequestHandler<ThrowingRequestHandler, ErrorRequestMsg, string>();
                // TelemetryRequestBehavior registered first → becomes outermost
                reg.RegisterBehavior<TelemetryRequestBehavior<ErrorRequestMsg, string>, ErrorRequestMsg, Task<string>>();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<MediatorException>(
            () => mediator.Request<ErrorRequestMsg, string>(
                new ErrorRequestMsg(), TestContext.Current.CancellationToken)
        );

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Contains("NetMediate.Request", activityErrors);
    }

    // ── TelemetryStreamBehavior — error path ──────────────────────────────────────────────────

    [Fact]
    public async Task TelemetryStreamBehavior_WhenInnerBehaviorThrows_ShouldSetActivityErrorAndRethrow()
    {
        var activityErrors = new System.Collections.Concurrent.ConcurrentQueue<string>();

        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NetMediateDiagnostics.ActivitySourceName,
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.Status == ActivityStatusCode.Error)
                    activityErrors.Enqueue(activity.OperationName);
            },
        };
        ActivitySource.AddActivityListener(listener);

        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterStreamHandler<NoopStreamHandler, ErrorStreamMsg, string>();
                // TelemetryStreamBehavior registered first → becomes outermost
                reg.RegisterBehavior<TelemetryStreamBehavior<ErrorStreamMsg, string>, ErrorStreamMsg, IAsyncEnumerable<string>>();
                // ThrowingStreamBehavior registered second → becomes innermost; throws synchronously
                reg.RegisterBehavior<ThrowingStreamBehavior<ErrorStreamMsg, string>, ErrorStreamMsg, IAsyncEnumerable<string>>();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        Assert.Throws<InvalidOperationException>(
            () => mediator.RequestStream<ErrorStreamMsg, string>(
                new ErrorStreamMsg(), TestContext.Current.CancellationToken)
        );

        Assert.Contains("NetMediate.Stream", activityErrors);
    }

    // ── MediatorException — construction via Send ─────────────────────────────────────────────

    [Fact]
    public async Task Send_WhenCommandHandlerThrows_ShouldWrapInMediatorException()
    {
        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterCommandHandler<ThrowingCommandHandler, ErrorCommandMsg>();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<MediatorException>(
            () => mediator.Send(new ErrorCommandMsg(), TestContext.Current.CancellationToken)
        );

        Assert.IsType<InvalidOperationException>(ex.InnerException);
        Assert.Equal(typeof(ErrorCommandMsg), ex.MessageType);
        Assert.Equal(typeof(ICommandHandler<ErrorCommandMsg>), ex.HandlerType);
    }

    // ── Notify(IEnumerable) ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Notify_Enumerable_ShouldDispatchAllMessages()
    {
        EnumNotifyTrace.Reset();

        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterNotificationHandler<EnumNotifyHandler, EnumNotifyMsg>();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        IEnumerable<EnumNotifyMsg> messages = [new("a"), new("b"), new("c")];
        await mediator.Notify(messages, TestContext.Current.CancellationToken);

        await WaitForAsync(() => EnumNotifyTrace.Count >= 3, TestContext.Current.CancellationToken);

        Assert.Equal(3, EnumNotifyTrace.Count);
    }

    // ── Send(IEnumerable) ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_Enumerable_ShouldInvokeHandlerForEachCommand()
    {
        EnumSendTrace.Reset();

        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterCommandHandler<EnumSendHandler, EnumSendMsg>();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        IEnumerable<EnumSendMsg> commands = [new("1"), new("2")];
        await mediator.Send(commands, TestContext.Current.CancellationToken);

        Assert.Equal(2, EnumSendTrace.Count);
    }

    // ── NetMediateDiagnostics.RecordSend (coverage of RecordSend) ─────────────────────────────

    [Fact]
    public void RecordSend_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(NetMediateDiagnostics.RecordSend<object>);
        Assert.Null(ex);
    }

    [Fact]
    public void RecordNotify_WhenMeterEnabled_EmitsCounter()
    {
        var dispatched = false;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.NotifyCountMetricName)
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => dispatched = true);
        meterListener.Start();

        NetMediateDiagnostics.RecordNotify<string>();

        Assert.True(dispatched);
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

    private static async Task<IHost> CreateHostAsync(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        configure(builder.Services);
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    // ── Message types ────────────────────────────────────────────────────────────────────────

    public sealed record ErrorNotificationMsg;
    public sealed record ErrorRequestMsg;
    public sealed record ErrorStreamMsg;
    public sealed record ErrorCommandMsg;
    public sealed record EnumNotifyMsg(string Value);
    public sealed record EnumSendMsg(string Value);

    // ── Trace helpers ─────────────────────────────────────────────────────────────────────────

    private static class EnumNotifyTrace
    {
        private static int _count;
        public static int Count => Volatile.Read(ref _count);
        public static void Increment() => Interlocked.Increment(ref _count);
        public static void Reset() => Interlocked.Exchange(ref _count, 0);
    }

    private static class EnumSendTrace
    {
        private static int _count;
        public static int Count => Volatile.Read(ref _count);
        public static void Increment() => Interlocked.Increment(ref _count);
        public static void Reset() => Interlocked.Exchange(ref _count, 0);
    }

    // ── Handlers ────────────────────────────────────────────────────────────────────────────

    private sealed class NoopNotificationHandler : INotificationHandler<ErrorNotificationMsg>
    {
        public Task Handle(ErrorNotificationMsg notification, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingRequestHandler : IRequestHandler<ErrorRequestMsg, string>
    {
        public Task<string> Handle(ErrorRequestMsg request, CancellationToken ct = default) =>
            Task.FromException<string>(new InvalidOperationException("request failure"));
    }

    private sealed class NoopStreamHandler : IStreamHandler<ErrorStreamMsg, string>
    {
        public async IAsyncEnumerable<string> Handle(
            ErrorStreamMsg query,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return "ok";
            await Task.CompletedTask;
        }
    }

    private sealed class ThrowingCommandHandler : ICommandHandler<ErrorCommandMsg>
    {
        public Task Handle(ErrorCommandMsg command, CancellationToken ct = default) =>
            Task.FromException(new InvalidOperationException("command failure"));
    }

    private sealed class EnumNotifyHandler : INotificationHandler<EnumNotifyMsg>
    {
        public Task Handle(EnumNotifyMsg notification, CancellationToken ct = default)
        {
            EnumNotifyTrace.Increment();
            return Task.CompletedTask;
        }
    }

    private sealed class EnumSendHandler : ICommandHandler<EnumSendMsg>
    {
        public Task Handle(EnumSendMsg command, CancellationToken ct = default)
        {
            EnumSendTrace.Increment();
            return Task.CompletedTask;
        }
    }

    // ── Pipeline behaviors ────────────────────────────────────────────────────────────────────

    private sealed class ThrowingNotificationBehavior<TMessage> : IPipelineBehavior<TMessage, Task>
        where TMessage : notnull
    {
        public Task Handle(TMessage message, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken ct = default) =>
            throw new InvalidOperationException("inner behavior failure");
    }

    private sealed class ThrowingStreamBehavior<TMessage, TResponse> : IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>>
        where TMessage : notnull
    {
        public IAsyncEnumerable<TResponse> Handle(
            TMessage message,
            PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> next,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("stream behavior failure");
    }
}
