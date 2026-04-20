using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Tests.Internals;

public sealed class DiagnosticsTelemetryTests
{
    [Fact]
    public async Task MediatorOperations_ShouldEmitActivitiesAndCounters()
    {
        using var fixture = new NetMediateFixture();

        var activityNames = new ConcurrentQueue<string>();
        var counterNames = new ConcurrentQueue<string>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NetMediateDiagnostics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => activityNames.Enqueue(activity.OperationName),
        };
        ActivitySource.AddActivityListener(activityListener);

        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == NetMediateDiagnostics.MeterName)
                listener.EnableMeasurementEvents(instrument);
        };
        meterListener.SetMeasurementEventCallback<long>(
            (instrument, _, _, _) => counterNames.Enqueue(instrument.Name)
        );
        meterListener.Start();

        await fixture.RunAsync(
            async sp =>
            {
                var mediator = sp.GetRequiredService<IMediator>();
                var ct = fixture.CancellationTokenSource.Token;

                await mediator.Send(new TestMessage("ok"), ct);
                _ = await mediator.Request<TestMessage, string>(new TestMessage("ok"), ct);
                await mediator.Notify(new TestMessage("ok"), ct);
                _ = await mediator.RequestStream<TestMessage, string>(new TestMessage("ok"), ct)
                    .AsyncToSync();
            }
        );
        await fixture.WaitAsync();

        var activityNamesSnapshot = activityNames.ToArray();
        var counterNamesSnapshot = counterNames.ToArray();

        Assert.Contains("NetMediate.Send", activityNamesSnapshot);
        Assert.Contains("NetMediate.Request", activityNamesSnapshot);
        Assert.Contains("NetMediate.Notify", activityNamesSnapshot);
        Assert.Contains("NetMediate.RequestStream", activityNamesSnapshot);

        Assert.Contains(NetMediateDiagnostics.SendCountMetricName, counterNamesSnapshot);
        Assert.Contains(NetMediateDiagnostics.RequestCountMetricName, counterNamesSnapshot);
        Assert.Contains(NetMediateDiagnostics.NotifyCountMetricName, counterNamesSnapshot);
        Assert.Contains(NetMediateDiagnostics.StreamCountMetricName, counterNamesSnapshot);
    }

    private sealed record TestMessage(string Name);

    private sealed class TestCommandHandler : ICommandHandler<TestMessage>
    {
        public Task Handle(TestMessage command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestRequestHandler : IRequestHandler<TestMessage, string>
    {
        public Task<string> Handle(TestMessage query, CancellationToken cancellationToken = default) =>
            Task.FromResult(query.Name);
    }

    private sealed class TestNotificationHandler : INotificationHandler<TestMessage>
    {
        public Task Handle(TestMessage notification, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestStreamHandler : IStreamHandler<TestMessage, string>
    {
        public async IAsyncEnumerable<string> Handle(
            TestMessage query,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default
        )
        {
            yield return query.Name;
            await Task.CompletedTask;
        }
    }
}
