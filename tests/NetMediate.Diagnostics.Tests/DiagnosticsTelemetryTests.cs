using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate.Diagnostics;

namespace NetMediate.Diagnostics.Tests;

internal static class AsyncExtensions
{
    public static async Task<IEnumerable<T>> AsyncToSync<T>(this IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (var item in values)
            result.Add(item);
        return result;
    }
}


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
            Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded,
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
        _ = await mediator.Request<TestMessage, string>(new("ok"), ct);
        await mediator.Notify(new TestMessage("ok"), ct);
        _ = await mediator.RequestStream<TestMessage, string>(new("ok"), ct)
            .AsyncToSync();

        var activityNamesSnapshot = activityNames.ToArray();
        var counterNamesSnapshot = counterNames.ToArray();

        Assert.Contains("NetMediate.Request", activityNamesSnapshot);
        Assert.Contains("NetMediate.Notify", activityNamesSnapshot);
        Assert.Contains("NetMediate.Stream", activityNamesSnapshot);

        Assert.Contains(NetMediateDiagnostics.RequestCountMetricName, counterNamesSnapshot);
        Assert.Contains(NetMediateDiagnostics.NotifyCountMetricName, counterNamesSnapshot);
        Assert.Contains(NetMediateDiagnostics.StreamCountMetricName, counterNamesSnapshot);
    }

    private static async Task<IHost> CreateHostAsync()
    {
        var builder = Host.CreateApplicationBuilder();
        // Telemetry behaviors are registered per-handler (no DI extension method needed).
        builder.Services.AddNetMediate(configure =>
        {
            configure.RegisterCommandHandler<TestCommandHandler, TestMessage>();
            configure.RegisterBehavior<TelemetryNotificationBehavior<TestMessage>, TestMessage, Task>();

            configure.RegisterRequestHandler<TestRequestHandler, TestMessage, string>();
            configure.RegisterBehavior<TelemetryRequestBehavior<TestMessage, string>, TestMessage, Task<string>>();

            configure.RegisterNotificationHandler<TestNotificationHandler, TestMessage>();
            // TelemetryNotificationBehavior<TestMessage> already registered above for the command.

            configure.RegisterStreamHandler<TestStreamHandler, TestMessage, string>();
            configure.RegisterBehavior<TelemetryStreamBehavior<TestMessage, string>, TestMessage, IAsyncEnumerable<string>>();
        });
        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
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
