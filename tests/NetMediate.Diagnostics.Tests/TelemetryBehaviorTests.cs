using System.Diagnostics;
using System.Diagnostics.Metrics;

[assembly: GenDI.GenDICoveration(false)]

namespace NetMediate.Diagnostics.Tests;

public sealed class TelemetryBehaviorTests
{
    private sealed record CommandMessage;
    private sealed record NotificationMessage;
    private sealed record RequestMessage;
    private sealed record Response(string Value);

    [Fact]
    public async Task TelemetryCommandBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TelemetryCommandBehavior<CommandMessage>();
        var called = false;

        await behavior.Handle(
            "key",
            new CommandMessage(),
            (_, _, _) =>
            {
                called = true;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken
        );

        Assert.True(called);
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal("NetMediate.Send", activity.OperationName);
        Assert.Equal("Send", activity.GetTagItem("netmediate.operation"));
        Assert.Equal(nameof(CommandMessage), activity.GetTagItem("netmediate.message_type"));
    }

    [Fact]
    public async Task TelemetryCommandBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TelemetryCommandBehavior<CommandMessage>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new CommandMessage(),
                (_, _, _) => Task.FromException(new InvalidOperationException("boom")),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public async Task TelemetryNotificationBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TelemetryNotificationBehavior<NotificationMessage>();

        await behavior.Handle(
            null,
            new NotificationMessage(),
            (_, _, _) => Task.CompletedTask,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("NetMediate.Notify", Assert.Single(stoppedActivities).OperationName);
    }

    [Fact]
    public async Task TelemetryNotificationBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TelemetryNotificationBehavior<NotificationMessage>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new NotificationMessage(),
                (_, _, _) => Task.FromException(new InvalidOperationException("boom")),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public async Task TelemetryRequestBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TelemetryRequestBehavior<RequestMessage, Response>();

        var response = await behavior.Handle(
            null,
            new RequestMessage(),
            (_, _, _) => Task.FromResult(new Response("ok")),
            TestContext.Current.CancellationToken
        );

        Assert.Equal("ok", response.Value);
        Assert.Equal("NetMediate.Request", Assert.Single(stoppedActivities).OperationName);
    }

    [Fact]
    public async Task TelemetryRequestBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TelemetryRequestBehavior<RequestMessage, Response>();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(
                null,
                new RequestMessage(),
                (_, _, _) => Task.FromException<Response>(new InvalidOperationException("boom")),
                TestContext.Current.CancellationToken
            )
        );

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public void StartActivity_WithParentActivity_CreatesLinkedActivityAndTags()
    {
        using var listener = CreateListener(out var stoppedActivities);
        using var parent = new Activity("parent").Start();

        using (var activity = NetMediateDiagnostics.StartActivity<CommandMessage>("Send"))
        {
            Assert.NotNull(activity);
            Assert.Equal("NetMediate.Send", activity.OperationName);
            Assert.Contains(activity.Links, link => link.Context.TraceId == parent.TraceId);
            Assert.Equal("Send", activity.GetTagItem("netmediate.operation"));
            Assert.Equal(nameof(CommandMessage), activity.GetTagItem("netmediate.message_type"));
        }

        Assert.Single(stoppedActivities);
    }

    [Fact]
    public void RecordNotify_WhenMeterEnabled_EmitsCounter()
    {
        var emitted = false;
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (
                instrument.Meter.Name == NetMediateDiagnostics.MeterName
                && instrument.Name == NetMediateDiagnostics.NotifyCountMetricName
            )
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            var hasMessageTypeTag = false;
            foreach (var tag in tags)
            {
                if (
                    tag.Key == NetMediateDiagnostics.MessageTypeName
                    && Equals(tag.Value, nameof(NotificationMessage))
                )
                {
                    hasMessageTypeTag = true;
                    break;
                }
            }

            emitted = value == 1 && hasMessageTypeTag;
        });
        meterListener.Start();

        NetMediateDiagnostics.RecordNotify<NotificationMessage>();

        Assert.True(emitted);
    }

    private static ActivityListener CreateListener(out List<Activity> stoppedActivities)
    {
        var activities = new List<Activity>();
        stoppedActivities = activities;
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NetMediateDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
