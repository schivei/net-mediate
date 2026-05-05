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
                reg.RegisterBehavior<
                    TelemetryNotificationBehavior<ErrorNotificationMsg>,
                    ErrorNotificationMsg,
                    Task
                >();
                // ThrowingBehavior registered second → becomes innermost; throws before handler is reached
                reg.RegisterBehavior<
                    ThrowingNotificationBehavior<ErrorNotificationMsg>,
                    ErrorNotificationMsg,
                    Task
                >();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            mediator.Notify(new ErrorNotificationMsg(), TestContext.Current.CancellationToken)
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
                reg.RegisterBehavior<
                    TelemetryRequestBehavior<ErrorRequestMsg, string>,
                    ErrorRequestMsg,
                    Task<string>
                >();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        var ex = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.Request<ErrorRequestMsg, string>(
                new ErrorRequestMsg(),
                TestContext.Current.CancellationToken
            )
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
                reg.RegisterBehavior<
                    TelemetryStreamBehavior<ErrorStreamMsg, string>,
                    ErrorStreamMsg,
                    IAsyncEnumerable<string>
                >();
                // ThrowingStreamBehavior registered second → becomes innermost; throws synchronously
                reg.RegisterBehavior<
                    ThrowingStreamBehavior<ErrorStreamMsg, string>,
                    ErrorStreamMsg,
                    IAsyncEnumerable<string>
                >();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();

        Assert.Throws<InvalidOperationException>(() =>
            mediator.RequestStream<ErrorStreamMsg, string>(
                new ErrorStreamMsg(),
                TestContext.Current.CancellationToken
            )
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

        var ex = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.Send(new ErrorCommandMsg(), TestContext.Current.CancellationToken)
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
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.NotifyCountMetricName
            )
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => dispatched = true);
        meterListener.Start();

        NetMediateDiagnostics.RecordNotify<string>();

        Assert.True(dispatched);
    }

    // ── Additional Record* enabled/disabled and StartActivity branches ─────────────────────────

    [Fact]
    public void RecordSend_WhenMeterEnabled_EmitsCounter()
    {
        var emitted = false;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.SendCountMetricName
            )
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => emitted = true);
        meterListener.Start();
        NetMediateDiagnostics.RecordSend<string>();
        Assert.True(emitted);
    }

    [Fact]
    public void RecordRequest_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(NetMediateDiagnostics.RecordRequest<object>);
        Assert.Null(ex);
    }

    [Fact]
    public void RecordRequest_WhenMeterEnabled_EmitsCounter()
    {
        var emitted = false;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.RequestCountMetricName
            )
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => emitted = true);
        meterListener.Start();
        NetMediateDiagnostics.RecordRequest<string>();
        Assert.True(emitted);
    }

    [Fact]
    public void RecordStream_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(NetMediateDiagnostics.RecordStream<object>);
        Assert.Null(ex);
    }

    [Fact]
    public void RecordStream_WhenMeterEnabled_EmitsCounter()
    {
        var emitted = false;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.StreamCountMetricName
            )
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => emitted = true);
        meterListener.Start();
        NetMediateDiagnostics.RecordStream<string>();
        Assert.True(emitted);
    }

    [Fact]
    public void RecordDispatch_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(NetMediateDiagnostics.RecordDispatch<object>);
        Assert.Null(ex);
    }

    [Fact]
    public void RecordDispatch_WhenMeterEnabled_EmitsCounter()
    {
        var emitted = false;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, l) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.DispatchCountMetricName
            )
                l.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => emitted = true);
        meterListener.Start();
        NetMediateDiagnostics.RecordDispatch<string>();
        Assert.True(emitted);
    }

    [Fact]
    public void RecordNotify_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(() => NetMediateDiagnostics.RecordNotify<object>());
        Assert.Null(ex);
    }

    // ── Multi-command-handler fan-out ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Send_WithTwoCommandHandlers_InvokesAll()
    {
        DiagMultiCmdTrace.Reset();

        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterCommandHandler<DiagMultiCmdHandlerA, DiagMultiCmdMessage>();
                reg.RegisterCommandHandler<DiagMultiCmdHandlerB, DiagMultiCmdMessage>();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Send(new DiagMultiCmdMessage("x"), TestContext.Current.CancellationToken);

        Assert.Equal(2, DiagMultiCmdTrace.Count);
    }

    // ── Multi-stream-handler fan-out ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RequestStream_WithTwoStreamHandlers_MergesItems()
    {
        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterStreamHandler<DiagStreamHandlerA, DiagStreamMessage, int>();
                reg.RegisterStreamHandler<DiagStreamHandlerB, DiagStreamMessage, int>();
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        var results = await mediator
            .RequestStream<DiagStreamMessage, int>(
                new DiagStreamMessage(),
                TestContext.Current.CancellationToken
            )
            .AsyncToSync();

        Assert.Equal([1, 2, 3, 4], [.. results]);
    }

    // ── Keyed handler registration/dispatch ────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterCommandHandler_WithKey_DispatchesToKeyedHandler()
    {
        DiagKeyedCmdTrace.Reset();

        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterCommandHandler<DiagNoopCmdHandler, DiagKeyedCmdMessage>();
                reg.RegisterCommandHandler<DiagKeyedCmdHandler, DiagKeyedCmdMessage>("dkey");
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Send(
            "dkey",
            new DiagKeyedCmdMessage("k"),
            TestContext.Current.CancellationToken
        );

        Assert.True(DiagKeyedCmdTrace.Called);
    }

    [Fact]
    public async Task RegisterRequestHandler_WithKey_DispatchesToKeyedHandler()
    {
        using var host = await CreateHostAsync(services =>
        {
            services.UseNetMediate(reg =>
            {
                reg.RegisterRequestHandler<DiagNoopReqHandler, DiagKeyedReqMessage, string>();
                reg.RegisterRequestHandler<DiagKeyedReqHandler, DiagKeyedReqMessage, string>(
                    "rkey"
                );
            });
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        var result = await mediator.Request<DiagKeyedReqMessage, string>(
            "rkey",
            new DiagKeyedReqMessage("v"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("v", result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────────

    private static async Task WaitForAsync(Func<bool> predicate, CancellationToken ct)
    {
        for (var i = 0; i < 200; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (predicate())
                return;
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

    public sealed record DiagMultiCmdMessage(string Value);

    public sealed record DiagStreamMessage;

    public sealed record DiagKeyedCmdMessage(string Value);

    public sealed record DiagKeyedReqMessage(string Value);

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

    private static class DiagMultiCmdTrace
    {
        private static int _count;
        public static int Count => Volatile.Read(ref _count);

        public static void Increment() => Interlocked.Increment(ref _count);

        public static void Reset() => Interlocked.Exchange(ref _count, 0);
    }

    private static class DiagKeyedCmdTrace
    {
        private static volatile bool _called;
        public static bool Called => _called;

        public static void Set() => _called = true;

        public static void Reset() => _called = false;
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
            [EnumeratorCancellation] CancellationToken ct = default
        )
        {
            yield return "ok";
            await Task.Yield();
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
        public Task Handle(
            object? key,
            TMessage message,
            PipelineBehaviorDelegate<TMessage, Task> next,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("inner behavior failure");
    }

    private sealed class ThrowingStreamBehavior<TMessage, TResponse>
        : IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>>
        where TMessage : notnull
    {
        public IAsyncEnumerable<TResponse> Handle(
            object? key,
            TMessage message,
            PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> next,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("stream behavior failure");
    }

    private sealed class DiagMultiCmdHandlerA : ICommandHandler<DiagMultiCmdMessage>
    {
        public Task Handle(DiagMultiCmdMessage command, CancellationToken ct = default)
        {
            DiagMultiCmdTrace.Increment();
            return Task.CompletedTask;
        }
    }

    private sealed class DiagMultiCmdHandlerB : ICommandHandler<DiagMultiCmdMessage>
    {
        public Task Handle(DiagMultiCmdMessage command, CancellationToken ct = default)
        {
            DiagMultiCmdTrace.Increment();
            return Task.CompletedTask;
        }
    }

    private sealed class DiagStreamHandlerA : IStreamHandler<DiagStreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            DiagStreamMessage query,
            [EnumeratorCancellation] CancellationToken ct = default
        )
        {
            yield return 1;
            yield return 2;
            await Task.Yield();
        }
    }

    private sealed class DiagStreamHandlerB : IStreamHandler<DiagStreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            DiagStreamMessage query,
            [EnumeratorCancellation] CancellationToken ct = default
        )
        {
            yield return 3;
            yield return 4;
            await Task.Yield();
        }
    }

    private sealed class DiagKeyedCmdHandler : ICommandHandler<DiagKeyedCmdMessage>
    {
        public Task Handle(DiagKeyedCmdMessage command, CancellationToken ct = default)
        {
            DiagKeyedCmdTrace.Set();
            return Task.CompletedTask;
        }
    }

    private sealed class DiagNoopCmdHandler : ICommandHandler<DiagKeyedCmdMessage>
    {
        public Task Handle(DiagKeyedCmdMessage command, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class DiagKeyedReqHandler : IRequestHandler<DiagKeyedReqMessage, string>
    {
        public Task<string> Handle(DiagKeyedReqMessage query, CancellationToken ct = default) =>
            Task.FromResult(query.Value);
    }

    private sealed class DiagNoopReqHandler : IRequestHandler<DiagKeyedReqMessage, string>
    {
        public Task<string> Handle(DiagKeyedReqMessage query, CancellationToken ct = default) =>
            Task.FromResult(string.Empty);
    }
}
