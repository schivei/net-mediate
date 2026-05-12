using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate;

/// <summary>
/// Provides an abstract base for executing notification handler pipelines for messages of a specified type.
/// </summary>
/// <remarks>This class enables the construction and execution of notification handler pipelines, supporting
/// extensibility through pipeline behaviors and keyed handler resolution. Derived types can customize pipeline
/// construction or execution as needed.</remarks>
/// <typeparam name="TMessage">The type of notification message to be processed. Must be non-null.</typeparam>
/// <param name="serviceProvider">The service provider used to resolve notification handlers and pipeline behaviors.</param>
/// <param name="logger">The logger used to record errors and diagnostic information during pipeline execution.</param>
public sealed class NotificationPipelineExecutor<TMessage>(IServiceProvider serviceProvider, ILogger<NotificationPipelineExecutor<TMessage>> logger)
    where TMessage : notnull
{
    // Cached pipeline for the common key-less path; built once on the first call.
    private PipelineBehaviorDelegate<TMessage, Task>? _noKeyPipeline;

    /// <summary>
    /// Invokes the notification handler pipeline for the specified message and key.
    /// </summary>
    /// <remarks>The handler pipeline is constructed lazily and executed for the provided message and key. The
    /// pipeline may include additional behaviors or middleware components depending on the configuration.</remarks>
    /// <param name="key">An optional key used to identify or differentiate the handler pipeline instance. May be null if no key is
    /// required.</param>
    /// <param name="message">The notification message to be processed by the handler pipeline.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation of handling the notification message.</returns>
    public Task Handle(
        object? key,
        TMessage message,
        CancellationToken cancellationToken
    )
    {
        if (key is null)
        {
            // Fast path: reuse the cached pipeline for the key-less case.
            var pipeline = _noKeyPipeline ?? InitNoKeyPipeline();
            return pipeline(null, message, cancellationToken);
        }

        return BuildPipeline(key)(key, message, cancellationToken);
    }

    private PipelineBehaviorDelegate<TMessage, Task> InitNoKeyPipeline()
    {
        var built = BuildPipeline(null);
        Interlocked.CompareExchange(ref _noKeyPipeline, built, null);
        return _noKeyPipeline!;
    }

    private INotificationHandler<TMessage>[] ResolveKeyedHandlers(object key)
    {
        var registry = serviceProvider.GetService<KeyedHandlerRegistry<INotificationHandler<TMessage>>>();
        if (registry is not null && registry.TryGetAll(key, serviceProvider, out var handlers) && handlers.Length > 0)
            return handlers;
        return [.. serviceProvider.GetServices<INotificationHandler<TMessage>>()];
    }

    private PipelineBehaviorDelegate<TMessage, Task> BuildPipeline(object? key)
    {
        var handlers = key is null
            ? [.. serviceProvider.GetServices<INotificationHandler<TMessage>>()]
            : ResolveKeyedHandlers(key);

        var behaviors = serviceProvider.GetServices<IPipelineNotificationBehavior<TMessage>>().ToArray();

        // Single-handler fast path: call the handler directly without any indirection.
        // Multi-handler path: fire each handler individually so the caller returns immediately
        // (true fire-and-forget per handler). Async handlers whose tasks are not yet completed
        // are individually observed via AwaitHandlerFault to ensure errors are always logged.
        PipelineBehaviorDelegate<TMessage, Task> app =
            handlers.Length == 1
                ? (_, msg, ct) => handlers[0].Handle(msg, ct)
                : (_, msg, ct) =>
                {
                    foreach (var h in handlers)
                    {
                        var t = h.Handle(msg, ct);
                        if (!t.IsCompletedSuccessfully)
                        {
#pragma warning disable CS4014 // intentional fire-and-forget: faults are logged inside AwaitHandlerFault
                            AwaitHandlerFault(t);
#pragma warning restore CS4014
                        }
                    }
                    return Task.CompletedTask;
                };

        // Wrap app with the registered behaviors. Behaviors are resolved once here and cached
        // with the pipeline — DI resolution happens only on the first Handle() call per key.
        var pipeline = behaviors.Length == 0
            ? app
            : behaviors.AsEnumerable().Reverse()
                .Aggregate(
                    app,
                    (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
                );

        return ErrorReporting;

        // Outer error-reporting shell. For notifications this is fire-and-forget: exceptions
        // are logged and silently absorbed so the returned Task is never faulted. This ensures
        // callers never need to observe the Task for error-handling purposes.
        Task ErrorReporting(object? routingKey, TMessage msg, CancellationToken ct)
        {
            var task = pipeline(routingKey, msg, ct);
            // Avoid async state-machine allocation on the hot success path.
            return task.IsCompletedSuccessfully ? task : AwaitAndCatch(task);

            async Task AwaitAndCatch(Task t)
            {
                try
                {
                    await t.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogFailure(ex);
                }
            }
        }

        void LogFailure(Exception ex)
        {
            logger.LogError(
                ex,
                "Error executing notification pipeline for message of type {MessageType}: {Message}",
                typeof(TMessage).FullName,
                ex.Message
            );
        }
    }

    // Observes an individual handler task for the multi-handler fire-and-forget path.
    // Called only when the handler task is not already completed, avoiding allocation on the hot path.
    // Uses OnlyOnFaulted so the logging callback is only invoked when the task faults; a successful
    // async completion produces no additional work. ExecuteSynchronously runs the callback inline
    // when the antecedent is already completed (e.g. Task.FromException), making fault logging
    // synchronous and deterministic in the common error path.
    private Task AwaitHandlerFault(Task t) =>
        t.ContinueWith(
            completed =>
            {
                var ex = completed.Exception!.GetBaseException();
                logger.LogError(
                    ex,
                    "Error executing notification pipeline for message of type {MessageType}: {Message}",
                    typeof(TMessage).FullName,
                    ex.Message
                );
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default
        );
}
