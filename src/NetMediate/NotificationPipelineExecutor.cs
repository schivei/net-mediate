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
    /// <summary>
    /// Invokes the notification handler pipeline for the specified message and key.
    /// </summary>
    /// <remarks>The handler pipeline is constructed lazily and executed for the provided message and key. The
    /// pipeline may include additional behaviors or middleware components depending on the configuration.</remarks>
    /// <param name="key">An optional key used to identify or differentiate the handler pipeline instance. May be null if no key is
    /// required.</param>
    /// <param name="message">The notification message to be processed by the handler pipeline.</param>
    /// <param name="exec">A delegate representing the next handler or pipeline stage to execute for the notification message.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation of handling the notification message.</returns>
    public Task Handle(
        object? key,
        TMessage message,
        HandlerExecutionDelegate<INotificationHandler<TMessage>, TMessage, Task> exec,
        CancellationToken cancellationToken
    )
    {
        var lazy = new Lazy<PipelineBehaviorDelegate<TMessage, Task>>(
            () => BuildPipeline(key, exec),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        return lazy.Value(key, message, cancellationToken);
    }

    private INotificationHandler<TMessage>[] ResolveKeyedHandlers(object key)
    {
        var registry = serviceProvider.GetService<KeyedHandlerRegistry<INotificationHandler<TMessage>>>();
        if (registry is not null && registry.TryGetAll(key, serviceProvider, out var handlers) && handlers.Length > 0)
            return handlers;
        return serviceProvider.GetServices<INotificationHandler<TMessage>>().ToArray();
    }

    private PipelineBehaviorDelegate<TMessage, Task> BuildPipeline(
        object? key,
        HandlerExecutionDelegate<INotificationHandler<TMessage>, TMessage, Task> exec
    )
    {
        var handlers = key is null
            ? serviceProvider.GetServices<INotificationHandler<TMessage>>().ToArray()
            : ResolveKeyedHandlers(key);

        var behaviors = serviceProvider.GetServices<IPipelineNotificationBehavior<TMessage>>().ToArray();

        PipelineBehaviorDelegate<TMessage, Task> app =
            handlers.Length == 1
                ? (_, msg, ct) => handlers[0].Handle(msg, ct)
                : (routingKey, msg, ct) => exec(routingKey, msg, handlers, ct);

        var pipeline = behaviors.Length == 0
            ? app
            : behaviors.AsEnumerable().Reverse()
                .Aggregate(
                    app,
                    (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
                );

        return ErrorReporting;

        async Task ErrorReporting(object? routingKey, TMessage msg, CancellationToken ct)
        {
            try
            {
                await pipeline(routingKey, msg, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (LogFailure(ex))
            {
                throw;
            }
        }

        bool LogFailure(Exception ex)
        {
            logger.LogError(
                ex,
                "Error executing notification pipeline for message of type {MessageType}: {Message}",
                typeof(TMessage).FullName,
                ex.Message
            );
            return true;
        }
    }
}
