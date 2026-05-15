using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace NetMediate.Quartz;

/// <inheritdoc/>
[RequiresDynamicCode(
    "QuartzNotificationJob uses MakeGenericMethod for per-type notification dispatch and is not compatible with NativeAOT."
)]
[RequiresUnreferencedCode(
    "QuartzNotificationJob uses reflection to resolve message types by name and dispatch notifications."
)]
[DecoratorFor<INotifiable>(Order = int.MinValue)]
internal sealed class QuartzNotifier : INotifiable
{
    /// <summary>
    /// Gets or sets the underlying <see cref="INotifiable"/> instance that this decorator wraps.
    /// </summary>
    /// <remarks>This property is required to fulfill the contract of <see cref="INotifiable"/> and enables
    /// the use of this class as a decorator. The property may not be used directly in typical scenarios.</remarks>
    [Inject] public required INotifiable Inner { get; init; }
    [Inject] internal IScheduler Scheduler { get; init; }
    [Inject] internal INotificationSerializer Serializer { get; init; }
    [Inject] internal IOptions<QuartzNotificationOptions> Options { get; init; }
    [Inject] internal ILogger<QuartzNotifier> Logger { get; init; }

    /// <inheritdoc />
    public async Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        var json = Serializer.Serialize(message);
        var typeName =
            typeof(TMessage).AssemblyQualifiedName
            ?? throw new InvalidOperationException(
                $"Cannot determine assembly-qualified name for type '{typeof(TMessage).FullName}'."
            );

        var jobKey = new JobKey($"{typeof(TMessage).Name}_{Guid.NewGuid():N}", Options.Value.GroupName);

        var jobBuilder = JobBuilder
            .Create<QuartzNotificationJob>()
            .WithIdentity(jobKey)
            .UsingJobData(QuartzNotificationJob.MessageDataKey, json)
            .UsingJobData(QuartzNotificationJob.TypeDataKey, typeName)
            .StoreDurably(false);

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
            .WithIdentity($"{jobKey.Name}_trigger", Options.Value.GroupName)
            .StartNow()
            .Build();

        await Scheduler.ScheduleJob(job, trigger, cancellationToken).ConfigureAwait(false);

        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug(
                "QuartzNotifier: scheduled notification job {JobKey} for message type {MessageType}.",
                jobKey,
                typeof(TMessage).Name
            );
        }
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
        if (handlers.Length == 0 && Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug(
                "QuartzNotifier: no handlers registered for notification type {MessageType}.",
                typeof(TMessage).Name
            );
        }

        foreach (var handler in handlers)
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
    }
}
