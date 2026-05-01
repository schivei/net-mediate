using System.ComponentModel.DataAnnotations;
using global::Quartz;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.Internals;

namespace NetMediate.Quartz;

/// <summary>
/// An <see cref="INotifiable"/> implementation that persists each notification as a Quartz job before dispatching.
/// </summary>
/// <remarks>
/// <para>
/// When a notification arrives, <see cref="QuartzNotifier"/> schedules it as a Quartz <see cref="IJob"/> through
/// the configured <see cref="IScheduler"/>. If the hosting process crashes before the job executes, Quartz (when
/// configured with a persistent store such as <c>AdoJobStore</c>) can recover and replay the job on the next
/// startup. In a multi-node cluster, Quartz distributes job execution across available nodes.
/// </para>
/// <para>
/// The job is scheduled with a <see cref="ITrigger"/> that fires immediately. The actual notification pipeline
/// is invoked inside <see cref="QuartzNotificationJob"/>, which is resolved from the DI container and calls
/// back into <see cref="DispatchNotifications{TMessage}"/> on this instance.
/// </para>
/// <para>
/// This class handles <em>only</em> notifications; commands, requests, and streams continue through the default
/// in-process channel.
/// </para>
/// </remarks>
public sealed class QuartzNotifier(
    IScheduler scheduler,
    INotificationSerializer serializer,
    QuartzNotificationOptions options,
    IServiceScopeFactory serviceScopeFactory,
    ILogger<QuartzNotifier> logger
) : INotifiable
{
    /// <inheritdoc />
    public async ValueTask Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull, INotification
    {
        var json = serializer.Serialize(message);
        var typeName = typeof(TMessage).AssemblyQualifiedName
                       ?? throw new InvalidOperationException(
                           $"Cannot determine assembly-qualified name for type '{typeof(TMessage).FullName}'.");

        var jobKey = new JobKey($"{typeof(TMessage).Name}_{Guid.NewGuid():N}", options.GroupName);

        var job = JobBuilder.Create<QuartzNotificationJob>()
            .WithIdentity(jobKey)
            .UsingJobData(QuartzNotificationJob.MessageDataKey, json)
            .UsingJobData(QuartzNotificationJob.TypeDataKey, typeName)
            .StoreDurably(false)
            .Build();

        var trigger = TriggerBuilder.Create()
            .WithIdentity($"{jobKey.Name}_trigger", options.GroupName)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger, cancellationToken).ConfigureAwait(false);

        logger.LogDebug(
            "QuartzNotifier: scheduled notification job {JobKey} for message type {MessageType}.",
            jobKey,
            typeof(TMessage).Name);
    }

    /// <inheritdoc />
    public async ValueTask Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default)
        where TMessage : notnull, INotification
    {
        foreach (var message in messages)
            await Notify(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the full notification pipeline for <typeparamref name="TMessage"/> in a new scope.
    /// </summary>
    /// <remarks>
    /// Called from <see cref="QuartzNotificationJob"/> after the job fires. Builds the pipeline using all
    /// registered <see cref="IValidationHandler{TMessage}"/>, <see cref="INotificationHandler{TMessage}"/>,
    /// and <see cref="INotificationBehavior{TMessage}"/> from DI, then executes it.
    /// </remarks>
    /// <inheritdoc />
    public async ValueTask DispatchNotifications<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull, INotification
    {
        using var scope = serviceScopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        var handlers = sp.GetServices<INotificationHandler<TMessage>>().ToList();
        if (handlers.Count == 0)
        {
            logger.LogDebug(
                "QuartzNotifier: no handlers registered for notification type {MessageType}.",
                typeof(TMessage).Name);
            return;
        }

        var validations = sp.GetServices<IValidationHandler<TMessage>>();
        var behaviors = sp.GetServices<INotificationBehavior<TMessage>>().ToList();

        NotificationHandlerDelegate<TMessage> core = async (msg, token) =>
        {
            await ValidateAsync(msg, validations, token).ConfigureAwait(false);
            foreach (var handler in handlers)
                await handler.Handle(msg, token).ConfigureAwait(false);
        };

        // Wrap behaviors in reverse so the first registered runs outermost.
        for (var i = behaviors.Count - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var inner = core;
            core = (msg, token) => behavior.Handle(msg, inner, token);
        }

        await core(message, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask ValidateAsync<TMessage>(
        TMessage message,
        IEnumerable<IValidationHandler<TMessage>> validations,
        CancellationToken cancellationToken)
        where TMessage : notnull, IMessage
    {
        if (message is IValidatable selfValidatable)
        {
            var result = await selfValidatable.ValidateAsync().ConfigureAwait(false);
            if (result != ValidationResult.Success)
                throw new MessageValidationException(result);
        }

        foreach (var v in validations)
        {
            var result = await v.ValidateAsync(message, cancellationToken).ConfigureAwait(false);
            if (result != ValidationResult.Success)
                throw new MessageValidationException(result);
        }
    }
}
