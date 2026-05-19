using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quartz;
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
[Injectable<IJob>(ServiceLifetime.Singleton, RegistrationMultiplicity = RegistrationMultiplicity.Multiple)]
internal sealed class QuartzNotificationJob : IJob
{
    /// <summary>
    /// Gets the service provider used to resolve the inner dispatch services.
    /// </summary>
    [Inject]
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>
    /// Gets the serializer used to deserialize persisted notification payloads.
    /// </summary>
    [Inject]
    public required INotificationSerializer Serializer { get; init; }

    /// <summary>
    /// Gets the logger used by this job.
    /// </summary>
    [Inject]
    public required ILogger<QuartzNotificationJob> Logger { get; init; }

    /// <summary>Key used to store the serialized message in the <see cref="JobDataMap"/>.</summary>
    public const string MessageDataKey = "netmediate_message";

    /// <summary>Key used to store the message CLR assembly-qualified type name in the <see cref="JobDataMap"/>.</summary>
    public const string TypeDataKey = "netmediate_type";

    /// <summary>Key used to store the JSON-serialized routing key in the <see cref="JobDataMap"/>.</summary>
    public const string KeyDataKey = "netmediate_key";

    /// <summary>Key used to store the routing key CLR assembly-qualified type name in the <see cref="JobDataMap"/>.</summary>
    public const string KeyTypeDataKey = "netmediate_key_type";

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<
        Type,
        Func<IServiceProvider, object?, object, CancellationToken, Task>
    > s_dispatcherCache = new();

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        var data = context.JobDetail.JobDataMap;
        var json = data.GetString(MessageDataKey);
        var typeName = data.GetString(TypeDataKey);

        if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(typeName))
        {
            Logger.LogWarning(
                "QuartzNotificationJob: missing message data in job {JobKey}.",
                context.JobDetail.Key
            );
            return;
        }

        var messageType = Type.GetType(typeName);
        if (messageType is null)
        {
            Logger.LogError(
                "QuartzNotificationJob: cannot resolve type '{TypeName}' for job {JobKey}.",
                typeName,
                context.JobDetail.Key
            );
            return;
        }

        var message = Serializer.Deserialize(json, messageType);
        if (message is null)
        {
            Logger.LogWarning(
                "QuartzNotificationJob: deserialized message is null for job {JobKey}.",
                context.JobDetail.Key
            );
            return;
        }

        object? routingKey = null;
        var keyJson = data.GetString(KeyDataKey);
        var keyTypeName = data.GetString(KeyTypeDataKey);
        if (!string.IsNullOrEmpty(keyJson) && !string.IsNullOrEmpty(keyTypeName))
        {
            var keyType = Type.GetType(keyTypeName);
            if (keyType is not null)
                routingKey = System.Text.Json.JsonSerializer.Deserialize(keyJson, keyType);
        }

        var dispatcher = s_dispatcherCache.GetOrAdd(messageType, BuildDispatcher);

        await dispatcher(ServiceProvider, routingKey, message, context.CancellationToken)
            .ConfigureAwait(false);
    }

    [ExcludeFromCodeCoverage]
    private static Func<IServiceProvider, object?, object, CancellationToken, Task> BuildDispatcher(
        Type messageType
    )
    {
        var method = typeof(QuartzNotificationJob).GetMethod(
            nameof(DispatchNotification),
            [
                typeof(IServiceProvider),
                typeof(object),
                typeof(object),
                typeof(CancellationToken)
            ]
        )
            ?? throw new MissingMethodException(
                typeof(QuartzNotificationJob).FullName,
                nameof(DispatchNotification)
            );

        method = method
            .MakeGenericMethod(messageType);

        return (serviceProvider, key, message, cancellationToken) =>
            (Task)method.Invoke(null, [serviceProvider, key, message, cancellationToken])!;
    }

    private static Task DispatchNotification<TMessage>(
        IServiceProvider serviceProvider,
        object? key,
        object message,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        var notifiable = serviceProvider.GetRequiredService<INotifiable>();
        INotificationHandler<TMessage>[] handlers = key is null
            ? [.. serviceProvider.GetServices<INotificationHandler<TMessage>>()]
            : [.. serviceProvider.GetKeyedServices<INotificationHandler<TMessage>>(key)];

        return notifiable.DispatchNotifications(
            key,
            (TMessage)message,
            handlers,
            cancellationToken
        );
    }
}
