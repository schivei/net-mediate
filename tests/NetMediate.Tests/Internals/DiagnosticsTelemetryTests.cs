using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests.Internals;

public sealed class DiagnosticsTelemetryTests
{
    [Fact]
    public async Task MediatorOperations_ShouldEmitActivitiesAndCounters()
    {
        using var host = await CreateHostAsync();
        var mediator = host.Services.GetRequiredService<IMediator>();

        var activityNames = new ConcurrentQueue<string>();
        var counterNames = new ConcurrentQueue<string>();

        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == NetMediateDiagnostics.ActivitySourceName,
            Sample = static (ref _) =>
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

        var ct = TestContext.Current.CancellationToken;

        await mediator.Send(new TestMessage("ok"), ct);
        _ = await mediator.Request<TestMessage, string>(new TestMessage("ok"), ct);
        await mediator.Notify(new TestMessage("ok"), ct);
        _ = await mediator.RequestStream<TestMessage, string>(new TestMessage("ok"), ct)
            .AsyncToSync();

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

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(typeof(DiagnosticsTelemetryTests).Assembly);
        builder.Services.AddScoped<ICommandHandler<TestMessage>, TestCommandHandler>();
        builder.Services.AddScoped<IRequestHandler<TestMessage, string>, TestRequestHandler>();
        builder.Services.AddScoped<INotificationHandler<TestMessage>, TestNotificationHandler>();
        builder.Services.AddScoped<IStreamHandler<TestMessage, string>, TestStreamHandler>();

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    private sealed record TestMessage(string Name) : ICommand, INotification, IRequest<string>, IStream<string>;

    private sealed class TestCommandHandler : ICommandHandler<TestMessage>
    {
        public ValueTask Handle(TestMessage command, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class TestRequestHandler : IRequestHandler<TestMessage, string>
    {
        public ValueTask<string> Handle(TestMessage query, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(query.Name);
    }

    private sealed class TestNotificationHandler : INotificationHandler<TestMessage>
    {
        public ValueTask Handle(TestMessage notification, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
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
