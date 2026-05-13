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
            var pipeline = _noKeyPipeline ?? InitNoKeyPipeline();
            return pipeline(null, message, cancellationToken);
        }

        return BuildPipeline(key)(key, message, cancellationToken);
    }

    /// <summary>
    /// Handles the specified notification message using the provided execution delegate.
    /// </summary>
    /// <param name="key">An optional key that identifies the notification context. May be null if no key is required.</param>
    /// <param name="message">The notification message to handle. Must not be null.</param>
    /// <param name="_">The delegate representing the next handler in the execution pipeline. This parameter is required for pipeline
    /// execution.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous handling operation.</returns>
    public Task Handle(
        object? key,
        TMessage message,
        HandlerExecutionDelegate<INotificationHandler<TMessage>, TMessage, Task> _,
        CancellationToken cancellationToken
    ) => Handle(key, message, cancellationToken);

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

        PipelineBehaviorDelegate<TMessage, Task> app;
        if (handlers.Length == 1)
            app = (_, msg, ct) => handlers[0].Handle(msg, ct);
        else if (behaviors.Length == 0)
            app = CreateFireAndForgetApp(handlers);
        else
            app = CreateWhenAllApp(handlers);

        var pipeline = behaviors.Length == 0
            ? app
            : behaviors.AsEnumerable().Reverse()
                .Aggregate(
                    app,
                    (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
                );

        return ErrorReporting;

        Task ErrorReporting(object? routingKey, TMessage msg, CancellationToken ct)
        {
            var task = pipeline(routingKey, msg, ct);
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
    }

    private void LogFailure(Exception ex)
    {
        logger.LogError(
            ex,
            "Error executing notification pipeline for message of type {MessageType}: {Message}",
            typeof(TMessage).FullName,
            ex.Message
        );
    }

    private PipelineBehaviorDelegate<TMessage, Task> CreateFireAndForgetApp(INotificationHandler<TMessage>[] handlers) =>
        (_, msg, ct) =>
        {
            foreach (var h in handlers)
            {
                var t = h.Handle(msg, ct);
                if (!t.IsCompletedSuccessfully)
                    AwaitHandlerFault(t);
            }
            return Task.CompletedTask;
        };

    private static PipelineBehaviorDelegate<TMessage, Task> CreateWhenAllApp(INotificationHandler<TMessage>[] handlers) =>
        (_, msg, ct) =>
        {
            var tasks = new Task[handlers.Length];
            for (var i = 0; i < handlers.Length; i++)
                tasks[i] = handlers[i].Handle(msg, ct);
            return Task.WhenAll(tasks);
        };

    private void AwaitHandlerFault(Task t) =>
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
