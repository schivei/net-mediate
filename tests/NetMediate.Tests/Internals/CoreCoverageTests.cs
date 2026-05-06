using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests.Internals;

/// <summary>
/// Targets specific lines/branches in src/NetMediate that are not exercised by other test classes.
/// </summary>
public sealed class CoreCoverageTests
{
    [Fact]
    public void MessageValidationException_Ctor_ExposesValidationResultAndMessage()
    {
        var result = new ValidationResult("Field is required", ["Field"]);

        var ex = new MessageValidationException(result);

        Assert.Equal("Field is required", ex.Message);
        Assert.Same(result, ex.ValidationResult);
    }

    [Fact]
    public void MessageValidationException_IsException()
    {
        var result = new ValidationResult("error");
        var ex = new MessageValidationException(result);

        Assert.IsType<Exception>(ex, exactMatch: false);
        Assert.Same(result, ex.ValidationResult);
        Assert.Equal("error", ex.Message);
    }

    [Fact]
    public void MediatorException_Ctor_ExposesAllProperties()
    {
        var inner = new InvalidOperationException("handler error");

        var ex = new MediatorException(
            typeof(string),
            typeof(ICommandHandler<string>),
            "trace-42",
            inner
        );

        Assert.Equal(typeof(string), ex.MessageType);
        Assert.Equal(typeof(ICommandHandler<string>), ex.HandlerType);
        Assert.Equal("trace-42", ex.TraceId);
        Assert.Same(inner, ex.InnerException);
        Assert.IsType<Exception>(ex, exactMatch: false);
        Assert.Contains("String", ex.Message);
    }

    [Fact]
    public void MediatorException_WithNullHandlerType_MessageExcludesHandlerType()
    {
        var inner = new Exception("fail");

        var ex = new MediatorException(typeof(int), null, null, inner);

        Assert.Null(ex.HandlerType);
        Assert.Null(ex.TraceId);
        Assert.Contains("Int32", ex.Message);
    }

    [Fact]
    public void RecordDispatch_WhenMeterDisabled_ReturnsEarly()
    {
        var ex = Record.Exception(NetMediateDiagnostics.RecordDispatch<object>);

        Assert.Null(ex);
    }

    [Fact]
    public void RecordDispatch_WhenMeterEnabled_EmitsCounter()
    {
        var dispatched = false;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.DispatchCountMetricName
            )
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => dispatched = true);
        meterListener.Start();

        NetMediateDiagnostics.RecordDispatch<string>();

        Assert.True(dispatched);
    }

    [Fact]
    public void RecordSend_WhenMeterEnabled_EmitsCounter()
    {
        var emitted = false;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.SendCountMetricName
            )
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => emitted = true);
        meterListener.Start();
        NetMediateDiagnostics.RecordSend<string>();
        Assert.True(emitted);
    }

    [Fact]
    public void RecordSend_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(NetMediateDiagnostics.RecordSend<object>);
        Assert.Null(ex);
    }

    [Fact]
    public void RecordRequest_WhenMeterEnabled_EmitsCounter()
    {
        var emitted = false;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.RequestCountMetricName
            )
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => emitted = true);
        meterListener.Start();
        NetMediateDiagnostics.RecordRequest<string>();
        Assert.True(emitted);
    }

    [Fact]
    public void RecordRequest_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(NetMediateDiagnostics.RecordRequest<object>);
        Assert.Null(ex);
    }

    [Fact]
    public void RecordStream_WhenMeterEnabled_EmitsCounter()
    {
        var emitted = false;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.StreamCountMetricName
            )
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => emitted = true);
        meterListener.Start();
        NetMediateDiagnostics.RecordStream<string>();
        Assert.True(emitted);
    }

    [Fact]
    public void RecordStream_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(NetMediateDiagnostics.RecordStream<object>);
        Assert.Null(ex);
    }

    [Fact]
    public void RecordNotify_WhenMeterDisabled_DoesNotThrow()
    {
        var ex = Record.Exception(() => NetMediateDiagnostics.RecordNotify<object>());
        Assert.Null(ex);
    }

    [Fact]
    public void StartActivity_WhenNoListeners_ReturnsNull()
    {
        Assert.False(
            NetMediateDiagnostics.ActivitySource.HasListeners(),
            "Expected no ActivityListeners to be active during this test."
        );

        var activity = NetMediateDiagnostics.StartActivity<string>("NoListenerOp");
        Assert.Null(activity);
    }

    [Fact]
    public async Task Send_WithMultipleCommandHandlers_ShouldInvokeAll()
    {
        MultiCmdTrace.Reset();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterCommandHandler<MultiCmdHandlerA, MultiCmdMessage>();
            reg.RegisterCommandHandler<MultiCmdHandlerB, MultiCmdMessage>();
        });
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Send(new MultiCmdMessage(), TestContext.Current.CancellationToken);

        Assert.Equal(2, MultiCmdTrace.Count);
    }

    [Fact]
    public async Task RegisterCommandHandler_WithKey_DispatchesToKeyedHandler()
    {
        KeyedCmdTrace.Reset();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterCommandHandler<NoopKeyCmdHandler, KeyedCmdMessage>();
            reg.RegisterCommandHandler<KeyedCmdHandler, KeyedCmdMessage>("make");
        });
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Send(
            "make",
            new KeyedCmdMessage(),
            TestContext.Current.CancellationToken
        );

        Assert.True(KeyedCmdTrace.Called);
    }

    [Fact]
    public async Task RegisterNotificationHandler_WithKey_DispatchesToKeyedHandler()
    {
        KeyedNotifTrace.Reset();

        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterNotificationHandler<NoopKeyNotifHandler, KeyedNotifMessage>();
            reg.RegisterNotificationHandler<KeyedNotifHandler, KeyedNotifMessage>("key");
        });
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Notify(
            "key",
            new KeyedNotifMessage(),
            TestContext.Current.CancellationToken
        );

        await WaitForAsync(() => KeyedNotifTrace.Called, TestContext.Current.CancellationToken);
        Assert.True(KeyedNotifTrace.Called);
    }

    [Fact]
    public async Task RegisterRequestHandler_WithKey_DispatchesToKeyedHandler()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterRequestHandler<NoopKeyReqHandler, KeyedReqMessage, string>();
            reg.RegisterRequestHandler<KeyedReqHandler, KeyedReqMessage, string>("rkey");
        });
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        var result = await mediator.Request<KeyedReqMessage, string>(
            "rkey",
            new KeyedReqMessage("val"),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("val", result);
    }

    [Fact]
    public async Task RegisterStreamHandler_WithKey_DispatchesToKeyedHandler()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterStreamHandler<NoopKeyStreamHandler, KeyedStreamMessage, int>();
            reg.RegisterStreamHandler<KeyedStreamHandler, KeyedStreamMessage, int>("sky");
        });
        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        var results = new List<int>();
        await foreach (
            var item in mediator.RequestStream<KeyedStreamMessage, int>(
                "sky",
                new KeyedStreamMessage(3),
                TestContext.Current.CancellationToken
            )
        )
            results.Add(item);

        Assert.Equal([1, 2, 3], results);
    }

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

    private sealed record MultiCmdMessage;

    private sealed record KeyedCmdMessage;

    private sealed record KeyedNotifMessage;

    private sealed record KeyedReqMessage(string Value);

    private sealed record KeyedStreamMessage(int Count);

    private static class MultiCmdTrace
    {
        private static int _count;
        public static int Count => Volatile.Read(ref _count);

        public static void Increment() => Interlocked.Increment(ref _count);

        public static void Reset() => Interlocked.Exchange(ref _count, 0);
    }

    private static class KeyedCmdTrace
    {
        private static volatile bool _called;
        public static bool Called => _called;

        public static void Set() => _called = true;

        public static void Reset() => _called = false;
    }

    private static class KeyedNotifTrace
    {
        private static volatile bool _called;
        public static bool Called => _called;

        public static void Set() => _called = true;

        public static void Reset() => _called = false;
    }

    private sealed class MultiCmdHandlerA : ICommandHandler<MultiCmdMessage>
    {
        public Task Handle(MultiCmdMessage command, CancellationToken ct = default)
        {
            MultiCmdTrace.Increment();
            return Task.CompletedTask;
        }
    }

    private sealed class MultiCmdHandlerB : ICommandHandler<MultiCmdMessage>
    {
        public Task Handle(MultiCmdMessage command, CancellationToken ct = default)
        {
            MultiCmdTrace.Increment();
            return Task.CompletedTask;
        }
    }

    private sealed class KeyedCmdHandler : ICommandHandler<KeyedCmdMessage>
    {
        public Task Handle(KeyedCmdMessage command, CancellationToken ct = default)
        {
            KeyedCmdTrace.Set();
            return Task.CompletedTask;
        }
    }

    private sealed class NoopKeyCmdHandler : ICommandHandler<KeyedCmdMessage>
    {
        public Task Handle(KeyedCmdMessage command, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class KeyedNotifHandler : INotificationHandler<KeyedNotifMessage>
    {
        public Task Handle(KeyedNotifMessage notification, CancellationToken ct = default)
        {
            KeyedNotifTrace.Set();
            return Task.CompletedTask;
        }
    }

    private sealed class NoopKeyNotifHandler : INotificationHandler<KeyedNotifMessage>
    {
        public Task Handle(KeyedNotifMessage notification, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class KeyedReqHandler : IRequestHandler<KeyedReqMessage, string>
    {
        public Task<string> Handle(KeyedReqMessage query, CancellationToken ct = default) =>
            Task.FromResult(query.Value);
    }

    private sealed class NoopKeyReqHandler : IRequestHandler<KeyedReqMessage, string>
    {
        public Task<string> Handle(KeyedReqMessage query, CancellationToken ct = default) =>
            Task.FromResult(string.Empty);
    }

    private sealed class KeyedStreamHandler : IStreamHandler<KeyedStreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            KeyedStreamMessage query,
            [EnumeratorCancellation] CancellationToken ct = default
        )
        {
            for (var i = 1; i <= query.Count; i++)
            {
                yield return i;
                await Task.Yield();
            }
        }
    }

    private sealed class NoopKeyStreamHandler : IStreamHandler<KeyedStreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            KeyedStreamMessage query,
            [EnumeratorCancellation] CancellationToken ct = default
        )
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
