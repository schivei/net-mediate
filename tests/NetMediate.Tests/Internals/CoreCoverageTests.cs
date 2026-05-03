using System.ComponentModel.DataAnnotations;
using System.Diagnostics.Metrics;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

/// <summary>
/// Targets specific lines/branches in src/NetMediate that are not exercised by other test classes.
/// </summary>
public sealed class CoreCoverageTests
{
    // ── ABaseHandler.Handle(object, ...) ─────────────────────────────────────────

    [Fact]
    public void ABaseHandler_HandleObjectOverload_DelegatesToTypedHandle()
    {
        var handler = new StringEchoHandler();

        // Call the untyped overload; it must cast and delegate to the typed Handle.
        object result = handler.Handle((object)"hello");

        Assert.Equal("hello", result);
    }

    [Fact]
    public void ABaseHandler_HandleObjectOverload_WithCancellation_DelegatesToTypedHandle()
    {
        var handler = new StringEchoHandler();

        object result = handler.Handle((object)"world", TestContext.Current.CancellationToken);

        Assert.Equal("world", result);
    }

    private sealed class StringEchoHandler : ABaseHandler<string, string>
    {
        public override string Handle(string message, CancellationToken cancellationToken = default) => message;
    }

    // ── MessageValidationException ────────────────────────────────────────────────

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

        Assert.IsAssignableFrom<Exception>(ex);
    }

    // ── NetMediateDiagnostics.RecordDispatch ──────────────────────────────────────

    [Fact]
    public void RecordDispatch_WhenMeterDisabled_ReturnsEarly()
    {
        // When no MeterListener subscribes to DispatchCount, Enabled==false and
        // the guard returns early without throwing. This covers the early-return branch.
        NetMediateDiagnostics.RecordDispatch<object>();
    }

    [Fact]
    public void RecordDispatch_WhenMeterEnabled_EmitsCounter()
    {
        var dispatched = false;

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.DispatchCountMetricName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>((_, _, _, _) => dispatched = true);
        meterListener.Start();

        NetMediateDiagnostics.RecordDispatch<string>();

        Assert.True(dispatched);
    }
}
