using global::Quartz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.Internals;

namespace NetMediate.Quartz;

/// <summary>
/// Quartz <see cref="IJob"/> implementation that deserializes and dispatches a stored notification message
/// through the NetMediate pipeline via <see cref="INotifiable.DispatchNotifications{TMessage}"/>.
/// </summary>
/// <remarks>
/// <para>
/// The job reads the serialized message and its CLR type name from <see cref="IJobExecutionContext.JobDetail"/>
/// <see cref="JobDataMap"/>, deserializes it using the registered <see cref="INotificationSerializer"/>,
/// and then calls <see cref="INotifiable.DispatchNotifications{TMessage}"/> via a cached generic delegate.
/// </para>
/// <para>
/// <see cref="DisallowConcurrentExecutionAttribute"/> is applied so that only one instance of the job runs at a
/// time per job key. For high-throughput scenarios consider enabling Quartz clustering and configuring a dedicated
/// thread pool.
/// </para>
/// </remarks>
[DisallowConcurrentExecution]
public sealed class QuartzNotificationJob(
    IServiceProvider serviceProvider,
    INotificationSerializer serializer,
    ILogger<QuartzNotificationJob> logger
) : IJob
{
    /// <summary>Key used to store the serialized message in the <see cref="JobDataMap"/>.</summary>
    public const string MessageDataKey = "netmediate_message";

    /// <summary>Key used to store the message CLR assembly-qualified type name in the <see cref="JobDataMap"/>.</summary>
    public const string TypeDataKey = "netmediate_type";

    // Cached delegate invoker keyed by message type to avoid per-call MakeGenericMethod on hot paths.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, Func<INotifiable, object, CancellationToken, ValueTask>>
        s_dispatcherCache = new();

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        var data = context.JobDetail.JobDataMap;
        var json = data.GetString(MessageDataKey);
        var typeName = data.GetString(TypeDataKey);

        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(typeName))
        {
            logger.LogWarning(
                "QuartzNotificationJob: missing message data in job {JobKey}.",
                context.JobDetail.Key);
            return;
        }

        var messageType = Type.GetType(typeName);
        if (messageType is null)
        {
            logger.LogError(
                "QuartzNotificationJob: cannot resolve type '{TypeName}' for job {JobKey}.",
                typeName,
                context.JobDetail.Key);
            return;
        }

        var message = serializer.Deserialize(json, messageType);
        if (message is null)
        {
            logger.LogWarning(
                "QuartzNotificationJob: deserialized message is null for job {JobKey}.",
                context.JobDetail.Key);
            return;
        }

        var notifiable = serviceProvider.GetRequiredService<INotifiable>();
        var dispatcher = s_dispatcherCache.GetOrAdd(messageType, BuildDispatcher);

        await dispatcher(notifiable, message, context.CancellationToken).ConfigureAwait(false);
    }

    private static Func<INotifiable, object, CancellationToken, ValueTask> BuildDispatcher(Type messageType)
    {
        var method = typeof(INotifiable)
            .GetMethod(nameof(INotifiable.DispatchNotifications))!
            .MakeGenericMethod(messageType);

        return (notifiable, message, cancellationToken) =>
            (ValueTask)method.Invoke(notifiable, [message, cancellationToken])!;
    }
}
