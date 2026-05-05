using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using NetMediate.Internals;
using Quartz;

namespace NetMediate.Quartz;

/// <inheritdoc/>
[RequiresDynamicCode(
    "QuartzNotificationJob uses MakeGenericMethod for per-type notification dispatch and is not compatible with NativeAOT."
)]
[RequiresUnreferencedCode(
    "QuartzNotificationJob uses reflection to resolve message types by name and dispatch notifications."
)]
public sealed class QuartzNotifier(
    IScheduler scheduler,
    INotificationSerializer serializer,
    QuartzNotificationOptions options,
    ILogger<QuartzNotifier> logger
) : INotifiable
{
    /// <inheritdoc />
    public async Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        var json = serializer.Serialize(message);
        var typeName =
            typeof(TMessage).AssemblyQualifiedName
            ?? throw new InvalidOperationException(
                $"Cannot determine assembly-qualified name for type '{typeof(TMessage).FullName}'."
            );

        var jobKey = new JobKey($"{typeof(TMessage).Name}_{Guid.NewGuid():N}", options.GroupName);

        var jobBuilder = JobBuilder
            .Create<QuartzNotificationJob>()
            .WithIdentity(jobKey)
            .UsingJobData(QuartzNotificationJob.MessageDataKey, json)
            .UsingJobData(QuartzNotificationJob.TypeDataKey, typeName)
            .StoreDurably(false);

        // Persist the routing key so that the job can replay the notification under the
        // same key when it fires. Only serialisable keys (primitives, strings, enums) are
        // supported; complex object keys are stored via their JSON representation.
        if (key is not null)
        {
            jobBuilder = jobBuilder
                .UsingJobData(
                    QuartzNotificationJob.KeyDataKey,
                    System.Text.Json.JsonSerializer.Serialize(key)
                )
                .UsingJobData(
                    QuartzNotificationJob.KeyTypeDataKey,
                    key.GetType().AssemblyQualifiedName ?? key.GetType().FullName ?? "System.Object"
                );
        }

        var job = jobBuilder.Build();

        var trigger = TriggerBuilder
            .Create()
            .WithIdentity($"{jobKey.Name}_trigger", options.GroupName)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger, cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "QuartzNotifier: scheduled notification job {JobKey} for message type {MessageType}.",
            jobKey,
            typeof(TMessage).Name
        );
    }

    /// <inheritdoc />
    public async Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        foreach (var message in messages)
            await Notify(key, message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0)
        {
            logger.LogDebug(
                "QuartzNotifier: no handlers registered for notification type {MessageType}.",
                typeof(TMessage).Name
            );
        }

        foreach (var handler in handlers)
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
    }
}
