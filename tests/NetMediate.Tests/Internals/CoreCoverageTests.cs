using System.Diagnostics.Metrics;

namespace NetMediate.Tests.Internals;

/// <summary>
/// Targets specific lines/branches in src/NetMediate that are not exercised by other test classes.
/// </summary>
public sealed class CoreCoverageTests
{
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

}
