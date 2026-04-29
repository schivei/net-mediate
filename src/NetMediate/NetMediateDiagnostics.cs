using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NetMediate;

/// <summary>
/// Provides diagnostic primitives (traces and metrics) emitted by NetMediate.
/// </summary>
public static class NetMediateDiagnostics
{
    /// <summary>
    /// Gets the ActivitySource name used by NetMediate traces.
    /// </summary>
    public const string ActivitySourceName = "NetMediate";

    /// <summary>
    /// Gets the Meter name used by NetMediate metrics.
    /// </summary>
    public const string MeterName = "NetMediate";

    /// <summary>
    /// Gets the metric name for Send operation count.
    /// </summary>
    public const string SendCountMetricName = "netmediate.send.count";

    /// <summary>
    /// Gets the metric name for Request operation count.
    /// </summary>
    public const string RequestCountMetricName = "netmediate.request.count";

    /// <summary>
    /// Gets the metric name for Notify operation count.
    /// </summary>
    public const string NotifyCountMetricName = "netmediate.notify.count";

    /// <summary>
    /// Gets the metric name for Stream operation count.
    /// </summary>
    public const string StreamCountMetricName = "netmediate.stream.count";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);
    private static readonly Meter s_meter = new(MeterName);
    private static readonly Counter<long> s_sendCount = s_meter.CreateCounter<long>(
        SendCountMetricName
    );
    private static readonly Counter<long> s_requestCount = s_meter.CreateCounter<long>(
        RequestCountMetricName
    );
    private static readonly Counter<long> s_notifyCount = s_meter.CreateCounter<long>(
        NotifyCountMetricName
    );
    private static readonly Counter<long> s_streamCount = s_meter.CreateCounter<long>(
        StreamCountMetricName
    );

    private const string MessageTypeTagKey = "message_type";

    internal static Activity? StartActivity<TMessage>(string operation)
    {
        if (!ActivitySource.HasListeners())
            return null;

        var activity = ActivitySource.StartActivity($"NetMediate.{operation}", ActivityKind.Internal);
        activity?.SetTag("netmediate.operation", operation);
        activity?.SetTag("netmediate.message_type", typeof(TMessage).Name);
        return activity;
    }

    internal static void RecordSend<TMessage>()
    {
        if (!s_sendCount.Enabled)
            return;

        s_sendCount.Add(1, new KeyValuePair<string, object?>(MessageTypeTagKey, typeof(TMessage).Name));
    }

    internal static void RecordRequest<TMessage>()
    {
        if (!s_requestCount.Enabled)
            return;

        s_requestCount.Add(
            1,
            new KeyValuePair<string, object?>(MessageTypeTagKey, typeof(TMessage).Name)
        );
    }

    internal static void RecordNotify<TMessage>()
    {
        if (!s_notifyCount.Enabled)
            return;

        s_notifyCount.Add(
            1,
            new KeyValuePair<string, object?>(MessageTypeTagKey, typeof(TMessage).Name)
        );
    }

    internal static void RecordStream<TMessage>()
    {
        if (!s_streamCount.Enabled)
            return;

        s_streamCount.Add(
            1,
            new KeyValuePair<string, object?>(MessageTypeTagKey, typeof(TMessage).Name)
        );
    }
}
