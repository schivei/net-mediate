using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Quartz;

/// <inheritdoc/>
[RequiresDynamicCode(
    "QuartzNotificationJob uses MakeGenericMethod for per-type notification dispatch and is not compatible with NativeAOT."
)]
[RequiresUnreferencedCode(
    "QuartzNotificationJob uses reflection to resolve message types by name and dispatch notifications."
)]
[DecoratorFor<IMediator>]
[ConditionalInjectable("")]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class QuartzMediator : IMediator
{
    /// <summary>
    /// Gets the inner mediator instance to which notifications will be delegated after being scheduled.
    /// </summary>
    [ExcludeFromCodeCoverage]
    [Inject] public required IMediator Inner { get; init; }
    /// <summary>
    /// Gets the Quartz scheduler instance.
    /// </summary>
    [Inject] public required ISchedulerFactory SchedulerFactory { get; init; }
    /// <summary>
    /// Gets the notification serializer instance.
    /// </summary>
    [Inject] public required INotificationSerializer Serializer { get; init; }
    /// <summary>
    /// Gets the Quartz notification options.
    /// </summary>
    [Inject] public required IOptions<QuartzNotificationOptions> Options { get; init; }
    /// <summary>
    /// Gets the logger instance.
    /// </summary>
    [Inject] public required ILogger<QuartzMediator> Logger { get; init; }

    private readonly record struct JobData<TMessage>(
        object? Key,
        TMessage Message,
        INotificationSerializer Serializer,
        ISchedulerFactory SchedulerFactory,
        QuartzNotificationOptions Options,
        ILogger Logger,
        JobKey JobKey
    ) where TMessage : notnull;

    private static async Task Notify<TMessage>(JobData<TMessage> jobData)
    {
        var typeName = typeof(TMessage).AssemblyQualifiedName!;

        var msg = jobData.Message as IQuartzMessage;

        var jobKey = jobData.JobKey;

        var scheduler = await jobData.SchedulerFactory.GetScheduler().ConfigureAwait(false);

        if (await scheduler.CheckExists(jobKey))
            return;

        var json = jobData.Serializer.Serialize(jobData.Message);

        var jobBuilder = JobBuilder
            .Create<QuartzNotificationJob<TMessage>>()
            .WithIdentity(jobKey)
            .UsingJobData(QuartzNotificationJob<TMessage>.MessageDataKey, json)
            .UsingJobData(QuartzNotificationJob<TMessage>.TypeDataKey, typeName)
            .StoreDurably(false);

        jobBuilder = MakeBuilder(jobData, jobBuilder);

        var job = jobBuilder
            .RequestRecovery(true)
            .Build();

        var trigger = BuildTrigger(jobData, jobKey, msg)
            .StartNow()
            .Build();

        await scheduler.ScheduleJob(job, trigger).ConfigureAwait(false);

        Log(jobData.Logger, jobKey, typeof(TMessage));
    }

    [ExcludeFromCodeCoverage]
    private static JobBuilder MakeBuilder<TMessage>(JobData<TMessage> jobData, JobBuilder jobBuilder)
    {
        if (jobData.Key is not null)
        {
            return jobBuilder
                .UsingJobData(
                    QuartzNotificationJob<TMessage>.KeyDataKey,
                    System.Text.Json.JsonSerializer.Serialize(jobData.Key)
                )
                .UsingJobData(
                    QuartzNotificationJob<TMessage>.KeyTypeDataKey,
                    jobData.Key.GetType().AssemblyQualifiedName ?? jobData.Key.GetType().FullName ?? "System.Object"
                );
        }

        return jobBuilder;
    }

    [ExcludeFromCodeCoverage]
    private static void Log(ILogger logger, JobKey jobKey, Type messageType)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "QuartzMediator: scheduled notification job {JobKey} for message type {MessageType}.",
                jobKey,
                messageType.Name
            );
        }
    }

    private static TriggerBuilder BuildTrigger<TMessage>(JobData<TMessage> jobData, JobKey jobKey, IQuartzMessage? message) where TMessage : notnull
    {
        var triggerBuilder = TriggerBuilder
            .Create()
            .WithIdentity($"{jobKey.Name}_trigger", message?.GroupName ?? jobData.Options.GroupName)
            .WithSimpleSchedule(s => s
                .WithInterval(TimeSpan.FromSeconds(1))
                .WithMisfireHandlingInstructionFireNow()
                .WithRepeatCount(jobData.Options.MisfireRetryCount)
            );

        return triggerBuilder;
    }

    /// <inheritdoc/>
    public void Notify<TMessage>(object? key, TMessage message)
    {
        var jobKey = Options.Value.GenerateId(message, Serializer);

        _ = Notify(new JobData<TMessage>(
            key,
            message,
            Serializer,
            SchedulerFactory,
            Options.Value,
            Logger,
            jobKey
        )).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Notify<TMessage>(TMessage message) where TMessage : notnull =>
        Notify(null, message);

    /// <inheritdoc/>
    public void Notifies<TMessage>(IEnumerable<TMessage> messages) where TMessage : notnull =>
        Notifies(null, messages);

    /// <inheritdoc/>
    public void Notifies<TMessage>(object? key, IEnumerable<TMessage> messages) where TMessage : notnull
    {
        if (!messages.Any())
            return;

        foreach (var m in messages)
            Notify(key, m);
    }

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public ValueTask<TResponse> Request<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Request<TMessage, TResponse>(message, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public ValueTask<TResponse> Request<TMessage, TResponse>(object? key, TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Request<TMessage, TResponse>(key, message, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.RequestStream<TMessage, TResponse>(message, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(object? key, TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.RequestStream<TMessage, TResponse>(key, message, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public ValueTask Send<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Send(message, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public ValueTask Send<TMessage>(object? key, TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Send(key, message, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public ValueTask Sends<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Sends(messages, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public ValueTask Sends<TMessage>(object? key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Send(key, messages, cancellationToken);
}
