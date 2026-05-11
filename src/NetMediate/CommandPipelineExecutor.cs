using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate;

/// <summary>
/// Provides an abstract base for executing command handler pipelines for messages of a specified type.
/// </summary>
/// <remarks>This class enables the construction and execution of command handler pipelines, supporting both keyed
/// and unkeyed scenarios. Derived types can customize pipeline composition and execution strategies. Thread safety and
/// error logging are handled internally.</remarks>
/// <typeparam name="TMessage">The type of command message to be processed. Must be non-null.</typeparam>
/// <param name="serviceProvider">The service provider used to resolve command handlers and pipeline behaviors.</param>
/// <param name="logger">The logger used to record errors and diagnostic information during pipeline execution.</param>
public sealed class CommandPipelineExecutor<TMessage>(IServiceProvider serviceProvider, ILogger<CommandPipelineExecutor<TMessage>> logger)
    where TMessage : notnull
{
    // Cached pipeline for the common key-less path; built once on the first call.
    private PipelineBehaviorDelegate<TMessage, Task>? _noKeyPipeline;

    /// <summary>
    /// Invokes the command handler pipeline for the specified message and key.
    /// </summary>
    /// <remarks>The pipeline is constructed lazily and executed for the provided message and key. The key
    /// parameter can be used to support keyed or contextual pipelines, depending on the implementation.</remarks>
    /// <param name="key">An optional key used to identify or differentiate the handler pipeline instance. May be null if not required by
    /// the pipeline.</param>
    /// <param name="message">The command message to be processed by the handler pipeline. Cannot be null.</param>
    /// <param name="exec">The delegate representing the next handler or the final command handler to execute in the pipeline.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous execution of the handler pipeline.</returns>
    public Task Handle(
        object? key,
        TMessage message,
        HandlerExecutionDelegate<ICommandHandler<TMessage>, TMessage, Task> exec,
        CancellationToken cancellationToken
    )
    {
        if (key is null)
        {
            // Fast path: reuse the cached pipeline for the key-less case.
            var pipeline = _noKeyPipeline ?? InitNoKeyPipeline(exec);
            return pipeline(null, message, cancellationToken);
        }

        return BuildPipeline(key, exec)(key, message, cancellationToken);
    }

    private PipelineBehaviorDelegate<TMessage, Task> InitNoKeyPipeline(
        HandlerExecutionDelegate<ICommandHandler<TMessage>, TMessage, Task> exec)
    {
        var built = BuildPipeline(null, exec);
        Interlocked.CompareExchange(ref _noKeyPipeline, built, null);
        return _noKeyPipeline!;
    }

    private ICommandHandler<TMessage>[] ResolveKeyedHandlers(object key)
    {
        var registry = serviceProvider.GetService<KeyedHandlerRegistry<ICommandHandler<TMessage>>>();
        if (registry is not null && registry.TryGetAll(key, serviceProvider, out var handlers) && handlers.Length > 0)
            return handlers;
        return [.. serviceProvider.GetServices<ICommandHandler<TMessage>>()];
    }

    private PipelineBehaviorDelegate<TMessage, Task> BuildPipeline(
        object? key,
        HandlerExecutionDelegate<ICommandHandler<TMessage>, TMessage, Task> exec
    )
    {
        var handlers = key is null
            ? [.. serviceProvider.GetServices<ICommandHandler<TMessage>>()]
            : ResolveKeyedHandlers(key);

        var behaviorArray = serviceProvider.GetServices<IPipelineCommandBehavior<TMessage>>().ToArray();

        var pipeline = behaviorArray.Length == 0
            ? App
            : behaviorArray.AsEnumerable().Reverse()
                .Aggregate<IPipelineCommandBehavior<TMessage>, PipelineBehaviorDelegate<TMessage, Task>>(
                    App,
                    (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
                );

        return ErrorReporting;

        Task App(object? routingKey, TMessage msg, CancellationToken ct) => exec(routingKey, msg, handlers, ct);

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
                catch (Exception ex) when (LogFailure(ex))
                {
                    throw;
                }
            }
        }

        bool LogFailure(Exception ex)
        {
            logger.LogError(
                ex,
                "Error executing Command pipeline for message of type {MessageType}: {Message}",
                typeof(TMessage).FullName,
                ex.Message
            );
            return true;
        }
    }
}
