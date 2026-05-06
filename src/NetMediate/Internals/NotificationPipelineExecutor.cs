using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

/// <summary>
/// Specialized pipeline executor for notification handlers.
/// Resolves <see cref="IPipelineBehavior{TMessage, Task}"/> (two-parameter),
/// <see cref="IPipelineBehavior{TMessage}"/> (one-parameter), and
/// <see cref="IPipelineNotificationBehavior{TMessage}"/> (notification shorthand) from DI,
/// without needing a runtime type switch.
/// Registered as a closed type by <c>RegisterNotificationHandler&lt;THandler, TMessage&gt;(...)</c>
/// so that no open-generic <c>typeof()</c> resolver is needed.
/// </summary>
internal sealed class NotificationPipelineExecutor<TMessage>(IServiceProvider serviceProvider, ILogger<NotificationPipelineExecutor<TMessage>> logger)
    where TMessage : notnull
{
    private static readonly ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<object, Lazy<PipelineBehaviorDelegate<TMessage, Task>>>
    > s_pipelineCache = new();

    static NotificationPipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp =>
        {
            if (s_pipelineCache.TryGetValue(sp, out var cache))
                cache.Clear();
            s_pipelineCache.Remove(sp);
        });

    public Task Handle(
        object? key,
        TMessage message,
        HandlerExecutionDelegate<INotificationHandler<TMessage>, TMessage, Task> exec,
        CancellationToken cancellationToken
    )
    {
        var perProvider = s_pipelineCache.GetValue(
            serviceProvider,
            _ => new ConcurrentDictionary<object, Lazy<PipelineBehaviorDelegate<TMessage, Task>>>()
        );

        var lazy = perProvider.GetOrAdd(
            key ?? Extensions.DEFAULT_ROUTING_KEY,
            _ => new Lazy<PipelineBehaviorDelegate<TMessage, Task>>(
                () => BuildPipeline(key, serviceProvider, exec, logger),
                LazyThreadSafetyMode.ExecutionAndPublication
            )
        );

        return lazy.Value(key, message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, Task> BuildPipeline(
        object? key,
        IServiceProvider sp,
        HandlerExecutionDelegate<INotificationHandler<TMessage>, TMessage, Task> exec,
        ILogger<NotificationPipelineExecutor<TMessage>> logger
    )
    {
        var handlers = sp.GetHandlers<INotificationHandler<TMessage>, TMessage, Task>(key)
            .ToArray();

        var behaviorArray = sp.GetCachedBehaviors<IPipelineBehavior<TMessage, Task>>()
            .Concat(
                sp.GetCachedBehaviors<IPipelineBehavior<TMessage>>()
            )
            .Concat(
                sp.GetCachedBehaviors<IPipelineNotificationBehavior<TMessage>>()
            )
            .ToArray();

        if (behaviorArray.Length == 0)
            return App;

        var pipeline = behaviorArray
            .Reverse()
            .Aggregate(
                (PipelineBehaviorDelegate<TMessage, Task>)App,
                (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
            );

        return ErrorReporting;

        Task App(object? routingKey, TMessage msg, CancellationToken ct) => exec(routingKey, msg, handlers, ct);

        Task ErrorReporting(object? routingKey, TMessage msg, CancellationToken ct)
        {
            var t = pipeline(routingKey, msg, ct);
            t.ContinueWith(
                tt =>
                {
                    logger.LogError(
                        tt.Exception,
                        "Error executing notification pipeline for message of type {MessageType}: {Message}",
                        typeof(TMessage).FullName, tt.Exception!.Message);
                },
                ct,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
            return t;
        }
    }
}
