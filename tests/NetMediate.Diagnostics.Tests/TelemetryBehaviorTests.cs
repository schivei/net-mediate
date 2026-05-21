using Microsoft.VisualBasic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;

[assembly: ExcludeFromCodeCoverage]
[assembly: GenDICoveration(false)]

namespace NetMediate.Diagnostics.Tests;

public sealed class TelemetryBehaviorTests
{
    private sealed record CommandMessage;
    private sealed record NotificationMessage;
    private sealed record RequestMessage;
    private sealed record Response(string Value);
    private sealed record StreamMessage;
    private sealed record StreamResponse(string Value);

    [Fact]
    public async Task TelemetryCommandBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var called = false;
        var behavior = new TestTelemetryCommandBehavior<CommandMessage>
        {
            Handler = new LambdaCommandHandler<CommandMessage>((_, _) =>
            {
                called = true;
                return ValueTask.CompletedTask;
            })
        };

        await behavior.Handle(new CommandMessage(), TestContext.Current.CancellationToken);

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
        var behavior = new TestTelemetryCommandBehavior<CommandMessage>
        {
            Handler = new LambdaCommandHandler<CommandMessage>((_, _) =>
                ValueTask.FromException(new InvalidOperationException("boom")))
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new CommandMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public async Task TelemetryNotificationBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TestTelemetryNotificationBehavior<NotificationMessage>
        {
            Handler = new LambdaNotificationHandler<NotificationMessage>((_, _) => ValueTask.CompletedTask)
        };

        await behavior.Handle(new NotificationMessage(), TestContext.Current.CancellationToken);
        Assert.Equal("NetMediate.Notify", Assert.Single(stoppedActivities).OperationName);
    }

    [Fact]
    public async Task TelemetryNotificationBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TestTelemetryNotificationBehavior<NotificationMessage>
        {
            Handler = new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                ValueTask.FromException(new InvalidOperationException("boom")))
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new NotificationMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public async Task TelemetryRequestBehavior_StartsAndStopsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TestTelemetryRequestBehavior<RequestMessage, Response>
        {
            Handler = new LambdaRequestHandler<RequestMessage, Response>((_, _) => ValueTask.FromResult(new Response("ok")))
        };

        var response = await behavior.Handle(new RequestMessage(), TestContext.Current.CancellationToken);

        Assert.Equal("ok", response.Value);
        Assert.Equal("NetMediate.Request", Assert.Single(stoppedActivities).OperationName);
    }

    [Fact]
    public async Task TelemetryRequestBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TestTelemetryRequestBehavior<RequestMessage, Response>
        {
            Handler = new LambdaRequestHandler<RequestMessage, Response>((_, _) =>
                ValueTask.FromException<Response>(new InvalidOperationException("boom")))
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            behavior.Handle(new RequestMessage(), TestContext.Current.CancellationToken).AsTask()
        );

        Assert.Equal(ActivityStatusCode.Error, Assert.Single(stoppedActivities).Status);
    }

    [Fact]
    public async Task TelemetryStreamBehavior_EmitsResponsesAndRecordsActivity()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TestTelemetryStreamBehavior<StreamMessage, StreamResponse>
        {
            Handler = new LambdaStreamHandler<StreamMessage, StreamResponse>((_, _) => Yield("one", "two"))
        };

        var responses = await behavior
            .Handle(new StreamMessage(), TestContext.Current.CancellationToken)
            .ToListAsync(TestContext.Current.CancellationToken);

        Assert.Equal(["one", "two"], responses.Select(response => response.Value).ToArray());
        Assert.Equal("NetMediate.Request", Assert.Single(stoppedActivities).OperationName);
    }

    [Fact]
    public async Task TelemetryStreamBehavior_RethrowsAndSetsErrorStatus()
    {
        using var listener = CreateListener(out var stoppedActivities);
        var behavior = new TestTelemetryStreamBehavior<StreamMessage, StreamResponse>
        {
            Handler = new LambdaStreamHandler<StreamMessage, StreamResponse>((_, _) => ThrowingStream())
        };

        await Assert.ThrowsAsync<InvalidOperationException>(behavior.Handle(new StreamMessage(), TestContext.Current.CancellationToken).Drain);

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
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
        };
        ActivitySource.AddActivityListener(listener);

        var activity = NetMediateDiagnostics.StartActivity<CommandMessage>("Send");
        Assert.Null(activity);
    }

    [Fact]
    public async Task TelemetryBehaviors_WhenNoActivityIsCreated_StillRethrowExceptions()
    {
        var commandBehavior = new TestTelemetryCommandBehavior<CommandMessage> {
            Handler = new LambdaCommandHandler<CommandMessage>((_, _) =>
                ValueTask.FromException(new InvalidOperationException("boom-command")))
        };
        var notificationBehavior = new TestTelemetryNotificationBehavior<NotificationMessage> {
            Handler = new LambdaNotificationHandler<NotificationMessage>((_, _) =>
                ValueTask.FromException(new InvalidOperationException("boom-notification")))
        };
        var requestBehavior = new TestTelemetryRequestBehavior<RequestMessage, Response> {
            Handler = new LambdaRequestHandler<RequestMessage, Response>((_, _) =>
                ValueTask.FromException<Response>(new InvalidOperationException("boom-request")))
        };
        var streamBehavior = new TestTelemetryStreamBehavior<StreamMessage, StreamResponse> {
            Handler = new LambdaStreamHandler<StreamMessage, StreamResponse>((_, _) => ThrowingStream())
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            commandBehavior.Handle(new CommandMessage(), TestContext.Current.CancellationToken).AsTask()
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            notificationBehavior.Handle(new NotificationMessage(), TestContext.Current.CancellationToken).AsTask()
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            requestBehavior.Handle(new RequestMessage(), TestContext.Current.CancellationToken).AsTask()
        );
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in streamBehavior.Handle(new StreamMessage(), TestContext.Current.CancellationToken))
            { }
        });
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
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => activities.Add(activity),
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private sealed class LambdaCommandHandler<TMessage>(
        Func<TMessage, CancellationToken, ValueTask> callback
    ) : ICommandHandler<TMessage>
        where TMessage : notnull
    {
        public ValueTask Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class LambdaNotificationHandler<TMessage>(
        Func<TMessage, CancellationToken, ValueTask> callback
    ) : INotificationHandler<TMessage>
        where TMessage : notnull
    {
        public ValueTask Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class LambdaRequestHandler<TMessage, TResponse>(
        Func<TMessage, CancellationToken, ValueTask<TResponse>> callback
    ) : IRequestHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        public ValueTask<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class LambdaStreamHandler<TMessage, TResponse>(
        Func<TMessage, CancellationToken, IAsyncEnumerable<TResponse>> callback
    ) : IStreamHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        public IAsyncEnumerable<TResponse> Handle(TMessage message, CancellationToken cancellationToken = default) =>
            callback(message, cancellationToken);
    }

    private sealed class TestTelemetryCommandBehavior<TMessage> : TelemetryCommandBehavior<TMessage>
        where TMessage : notnull;

    private sealed class TestTelemetryNotificationBehavior<TMessage> : TelemetryNotificationBehavior<TMessage>
        where TMessage : notnull;

    private sealed class TestTelemetryRequestBehavior<TMessage, TResponse> : TelemetryRequestBehavior<TMessage, TResponse>
        where TMessage : notnull;

    private sealed class TestTelemetryStreamBehavior<TMessage, TResponse> : TelemetryStreamBehavior<TMessage, TResponse>
        where TMessage : notnull;

    private static async IAsyncEnumerable<StreamResponse> Yield(params string[] values)
    {
        foreach (var value in values)
        {
            await Task.Yield();
            yield return new StreamResponse(value);
        }
    }

    private static async IAsyncEnumerable<StreamResponse> ThrowingStream()
    {
        yield return new(string.Empty);

        throw new InvalidOperationException("boom");
    }
}
