using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
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
public sealed class QuartzMediator : IMediator
{
    /// <summary>
    /// Gets the underlying mediator instance.
    /// </summary>
    [Inject] public required IMediator Inner { get; init; }
    /// <summary>
    /// Gets the Quartz scheduler instance.
    /// </summary>
    [Inject] public required IScheduler Scheduler { get; init; }
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

    /// <inheritdoc/>
    public Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Notify(null, message, cancellationToken);

    /// <inheritdoc/>
    public async Task Notify<TMessage>(object? key, TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull
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
                "QuartzMediator: scheduled notification job {JobKey} for message type {MessageType}.",
                jobKey,
                typeof(TMessage).Name
            );
        }
    }

    /// <inheritdoc/>
    public Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Notify(null, messages, cancellationToken);

    /// <inheritdoc/>
    public async Task Notify<TMessage>(object? key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        var tasks = new List<Task>();
        foreach (var message in messages)
            tasks.Add(Notify(key, message, cancellationToken));

        await Task.WhenAll(tasks).ConfigureAwait(false);

    }

    /// <inheritdoc/>
    public Task<TResponse> Request<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Request<TMessage, TResponse>(message, cancellationToken);

    /// <inheritdoc/>
    public Task<TResponse> Request<TMessage, TResponse>(object? key, TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Request<TMessage, TResponse>(key, message, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.RequestStream<TMessage, TResponse>(message, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(object? key, TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.RequestStream<TMessage, TResponse>(key, message, cancellationToken);

    /// <inheritdoc/>
    public Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Send(message, cancellationToken);

    /// <inheritdoc/>
    public Task Send<TMessage>(object? key, TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Send(key, message, cancellationToken);

    /// <inheritdoc/>
    public Task Send<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Send(messages, cancellationToken);

    /// <inheritdoc/>
    public Task Send<TMessage>(object? key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Inner.Send(key, messages, cancellationToken);
}
