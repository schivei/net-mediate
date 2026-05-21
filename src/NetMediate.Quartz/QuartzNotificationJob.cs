using Microsoft.Extensions.DependencyInjection;
using Quartz;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

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
/// <para>
/// This class uses reflection (<see cref="System.Reflection.MethodInfo.MakeGenericMethod"/>) to build per-type
/// dispatch delegates at runtime. It is not compatible with NativeAOT or trimming.
/// </para>
/// </remarks>
[DisallowConcurrentExecution]
[RequiresDynamicCode(
    "QuartzNotificationJob uses MakeGenericMethod for per-type notification dispatch and is not compatible with NativeAOT."
)]
[RequiresUnreferencedCode(
    "QuartzNotificationJob uses reflection to resolve message types by name and dispatch notifications."
)]
internal sealed class QuartzNotificationJob<TMessage>(
    IServiceProvider serviceProvider,
    INotificationSerializer serializer,
    INotifiable notifier
) : IJob where TMessage : notnull
{
    /// <summary>Key used to store the serialized message in the <see cref="JobDataMap"/>.</summary>
    public const string MessageDataKey = "netmediate_message";

    /// <summary>Key used to store the message CLR assembly-qualified type name in the <see cref="JobDataMap"/>.</summary>
    public const string TypeDataKey = "netmediate_type";

    /// <summary>Key used to store the JSON-serialized routing key in the <see cref="JobDataMap"/>.</summary>
    public const string KeyDataKey = "netmediate_key";

    /// <summary>Key used to store the routing key CLR assembly-qualified type name in the <see cref="JobDataMap"/>.</summary>
    public const string KeyTypeDataKey = "netmediate_key_type";

    private static readonly ConcurrentDictionary<ValueTuple<Type, object?>, Lazy<ImmutableArray<INotificationHandler<TMessage>>>> s_ntfCache = new();

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        var data = context.JobDetail.JobDataMap;
        var json = data.GetString(MessageDataKey);
        var typeName = data.GetString(TypeDataKey);
        var messageType = Type.GetType(typeName);

        var message = (TMessage)serializer.Deserialize(json, messageType);

        var keyJson = data.TryGetString(KeyDataKey, out var valueKey) ? valueKey : null;
        var keyTypeName = data.TryGetString(KeyTypeDataKey, out var typeKey) ? typeKey : null;
        object? routingKey = null;
        if (!string.IsNullOrEmpty(keyJson) && !string.IsNullOrEmpty(keyTypeName))
        {
            var keyType = Type.GetType(keyTypeName);
            if (keyType is not null)
                routingKey = System.Text.Json.JsonSerializer.Deserialize(keyJson, keyType);
        }

        var handlers = ResolveNotifyHandlers(routingKey);

        await notifier.DispatchNotifications(routingKey, message, [.. handlers], context.CancellationToken).ConfigureAwait(false);
    }

    private ImmutableArray<INotificationHandler<TMessage>> ResolveNotifyHandlers(object? key) =>
        s_ntfCache.GetOrAdd(
            (typeof(TMessage), key),
            _ => new Lazy<ImmutableArray<INotificationHandler<TMessage>>>(() => key is null ? [.. serviceProvider.GetServices<INotificationHandler<TMessage>>()] : [.. serviceProvider.GetKeyedServices<INotificationHandler<TMessage>>(key)])
        ).Value;
}
