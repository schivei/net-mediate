using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

[assembly: ExcludeFromCodeCoverage]
[assembly: GenDICoveration(false)]

namespace NetMediate.Diagnostics.Tests;

public sealed class TelemetryBehaviorTests
{
    [Fact]
    public async Task TelemetryCommandBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var provider = MakeProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        var msg = new CommandMessage();

        await mediator.SendCommandMessageAsync(msg, TestContext.Current.CancellationToken);

        Assert.True(msg.Called);
        var activity = Assert.Single(stoppedActivities);
        Assert.Equal("NetMediate.Send", activity.OperationName);
        Assert.Equal("Send", activity.GetTagItem("netmediate.operation"));
        Assert.Equal(nameof(CommandMessage), activity.GetTagItem("netmediate.message_type"));
    }

    [Fact]
    public async Task TelemetryCommandBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);

        var provider = MakeProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        var msg = new CommandMessage
        {
            Exception = new InvalidOperationException("boom")
        };

        var ex = await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.SendCommandMessageAsync(msg, TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Equal(msg.Exception, ex.InnerException);
        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public async Task TelemetryNotificationBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);

        var sem = new CountdownEvent(1);

        var provider = MakeProvider(sem);

        var mediator = provider.GetRequiredService<IMediator>();

        var msg = new NotificationMessage();

        mediator.NotifyNotificationMessage(msg);

        sem.Wait(TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        await Task.Yield();

        Assert.Equal("NetMediate.Notify", Assert.Single(stoppedActivities).OperationName);
    }

    [Fact]
    public async Task TelemetryNotificationBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);

        var sem = new CountdownEvent(1);

        var provider = MakeProvider(sem);

        var mediator = provider.GetRequiredService<IMediator>();

        var msg = new NotificationMessage
        {
            Exception = new InvalidOperationException("boom")
        };

        mediator.NotifyNotificationMessage(msg);

        sem.Wait(TestContext.Current.CancellationToken);

        await Task.Delay(100, TestContext.Current.CancellationToken);

        await Task.Yield();

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public async Task TelemetryRequestBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);

        var provider = MakeProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        var response = await mediator.RequestRequestMessageAsync(new(), TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Value);
        Assert.Equal("NetMediate.Request", Assert.Single(stoppedActivities).OperationName);
    }

    [Fact]
    public async Task TelemetryRequestBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);

        var provider = MakeProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<MediatorException>(() =>
            mediator.RequestRequestMessageAsync(new()
            {
                Exception = new InvalidOperationException("boom")
            }, TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public async Task TelemetryStreamBehavior_EmitsResponsesAndRecordsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);

        var provider = MakeProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        var responses = await mediator.StreamStreamMessageAsync(new StreamMessage(), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["ok", "done"], [.. responses.Select(response => response.Value)]);
        Assert.Equal("NetMediate.Request", Assert.Single(stoppedActivities).OperationName);
    }

    [Fact]
    public async Task TelemetryStreamBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);

        var provider = MakeProvider();

        var mediator = provider.GetRequiredService<IMediator>();

        await Assert.ThrowsAsync<InvalidOperationException>(mediator.StreamStreamMessageAsync(new()
        {
            Exception = new InvalidOperationException("boom")
        }, TestContext.Current.CancellationToken).Drain);

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
    public void StartActivity_WhenSamplingDisablesCreation_ReturnsNull()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NetMediateDiagnostics.ActivitySourceName,
            Sample = static (ref _) => ActivitySamplingResult.None,
        };
        ActivitySource.AddActivityListener(listener);

        var activity = NetMediateDiagnostics.StartActivity<CommandMessage>("Send");
        Assert.Null(activity);
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
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith(NetMediateDiagnostics.ActivitySourceName),
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity)
        };
        ActivitySource.AddActivityListener(listener);
        stoppedActivities = activities;
        return listener;
    }

    private static IServiceProvider MakeProvider(CountdownEvent? semaphore = null)
    {
        var services = new ServiceCollection();
        services.Clear();
        services.AddLogging();
        var configuration = new ConfigurationManager();
        services.AddSingleton<IConfiguration>(configuration);

        if (semaphore != null)
            services.AddSingleton(semaphore);

        NetMediate.DependencyInjection.GenDIServiceCollectionExtensions.AddGenDIServices(services);
        services.AddNetMediate();
        return services.BuildServiceProvider();
    }
}
