using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NetMediate;

/// <summary>
/// Provides diagnostic primitives (traces and metrics) emitted by NetMediate.
/// </summary>
public static class NetMediateDiagnostics
{
    /// <summary>Gets the ActivitySource name used by NetMediate traces.</summary>
    public const string ActivitySourceName = "NetMediate";

    /// <summary>Gets the Meter name used by NetMediate metrics.</summary>
    public const string MeterName = "NetMediate";

    /// <summary>Gets the metric name for Send operation count.</summary>
    public const string SendCountMetricName = "netmediate.send.count";

    /// <summary>Gets the metric name for Request operation count.</summary>
    public const string RequestCountMetricName = "netmediate.request.count";

    /// <summary>Gets the metric name for Notify operation count.</summary>
    public const string NotifyCountMetricName = "netmediate.notify.count";

    /// <summary>Gets the metric name for Dispatch operation count.</summary>
    public const string DispatchCountMetricName = "netmediate.dispatch.count";

    /// <summary>Gets the metric name for Stream operation count.</summary>
    public const string StreamCountMetricName = "netmediate.stream.count";

    /// <summary>Represents the name of the message type used for identification or serialization purposes.</summary>
    public const string MessageTypeName = "message_type";

    /// <summary>The ActivitySource instance used to create activities.</summary>
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    private static readonly Meter s_meter = new(MeterName);
    private static readonly Counter<long> s_sendCount = s_meter.CreateCounter<long>(SendCountMetricName);
    private static readonly Counter<long> s_requestCount = s_meter.CreateCounter<long>(RequestCountMetricName);
    private static readonly Counter<long> s_notifyCount = s_meter.CreateCounter<long>(NotifyCountMetricName);
    private static readonly Counter<long> s_streamCount = s_meter.CreateCounter<long>(StreamCountMetricName);
    private static readonly Counter<long> s_dispatchCount = s_meter.CreateCounter<long>(DispatchCountMetricName);

    /// <summary>Starts a new activity for the given message type and operation.</summary>
    public static Activity? StartActivity<TMessage>(string operation)
    {
        if (!ActivitySource.HasListeners())
            return null;

        // Capture a link to the ambient activity at dispatch time so that the mediator span
        // is correctly connected to the caller's trace — especially important for
        // fire-and-forget notifications where Activity.Current may differ at handler execution time.
        var links = Activity.Current is { } parent
            ? (IEnumerable<ActivityLink>)[new ActivityLink(parent.Context)]
            : null;

        var activity = ActivitySource.StartActivity(
            $"NetMediate.{operation}",
            ActivityKind.Internal,
            parentContext: default,
            links: links);

        activity?.SetTag("netmediate.operation", operation);
        activity?.SetTag("netmediate.message_type", typeof(TMessage).Name);
        return activity;
    }

    /// <summary>Records a Send metric increment.</summary>
    public static void RecordSend<TMessage>()
    {
        if (!s_sendCount.Enabled) return;
        s_sendCount.Add(1, new KeyValuePair<string, object?>(MessageTypeName, typeof(TMessage).Name));
    }

    /// <summary>Records a Request metric increment.</summary>
    public static void RecordRequest<TMessage>()
    {
        if (!s_requestCount.Enabled) return;
        s_requestCount.Add(1, new KeyValuePair<string, object?>(MessageTypeName, typeof(TMessage).Name));
    }

    /// <summary>Records a Notify metric increment.</summary>
    public static void RecordNotify<TMessage>(long size = 1)
    {
        if (!s_notifyCount.Enabled) return;
        s_notifyCount.Add(size, new KeyValuePair<string, object?>(MessageTypeName, typeof(TMessage).Name));
    }

    /// <summary>Records a Dispatch metric increment.</summary>
    public static void RecordDispatch<TMessage>()
    {
        if (!s_dispatchCount.Enabled) return;
        s_dispatchCount.Add(1, new KeyValuePair<string, object?>(MessageTypeName, typeof(TMessage).Name));
    }

    /// <summary>Records a Stream metric increment.</summary>
    public static void RecordStream<TMessage>()
    {
        if (!s_streamCount.Enabled) return;
        s_streamCount.Add(1, new KeyValuePair<string, object?>(MessageTypeName, typeof(TMessage).Name));
    }
}
