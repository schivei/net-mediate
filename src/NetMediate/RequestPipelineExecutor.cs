using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate;

/// <summary>
/// Executes a request handler pipeline for a specified message and returns the response asynchronously.
/// </summary>
/// <remarks>The request pipeline is cached per service provider and key to improve performance for repeated
/// invocations. If the same key and service provider are used, the pipeline instance is reused for subsequent
/// calls.</remarks>
/// <typeparam name="TMessage">The type of the message to be handled by the request pipeline. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the request handler pipeline.</typeparam>
/// <param name="serviceProvider">The service provider used to resolve request handlers and pipeline behaviors.</param>
/// <param name="logger">The logger used to record errors and diagnostic information during pipeline execution.</param>
public sealed class RequestPipelineExecutor<TMessage, TResponse>(IServiceProvider serviceProvider, ILogger<RequestPipelineExecutor<TMessage, TResponse>> logger)
    where TMessage : notnull
{
    /// <summary>
    /// Invokes the request handler pipeline for the specified message and returns the response asynchronously.
    /// </summary>
    /// <remarks>The pipeline is cached per service provider and key to optimize repeated invocations. If the
    /// same key and service provider are used, the pipeline is reused for subsequent calls.</remarks>
    /// <param name="key">An optional key used to identify the pipeline instance. If null, a default routing key is used.</param>
    /// <param name="message">The message to be handled by the request handler pipeline.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A task that represents the asynchronous operation. The task result contains the response produced by the request
    /// handler.</returns>
    public Task<TResponse> Handle(
        object? key,
        TMessage message,
        CancellationToken cancellationToken
    )
    {
        var lazy = new Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>(
            () => BuildPipeline(key),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        return lazy.Value(key, message, cancellationToken);
    }

    private IRequestHandler<TMessage, TResponse>[] ResolveKeyedHandlers(object key)
    {
        var registry = serviceProvider.GetService<KeyedHandlerRegistry<IRequestHandler<TMessage, TResponse>>>();
        if (registry is not null && registry.TryGet(key, out var keyed) && keyed is not null)
            return [keyed];
        return serviceProvider.GetServices<IRequestHandler<TMessage, TResponse>>().ToArray();
    }

    private PipelineBehaviorDelegate<TMessage, Task<TResponse>> BuildPipeline(object? key)
    {
        var handlers = key is null
            ? serviceProvider.GetServices<IRequestHandler<TMessage, TResponse>>().ToArray()
            : ResolveKeyedHandlers(key);

        var handler = handlers.Single();

        var behaviors = serviceProvider.GetServices<IPipelineRequestBehavior<TMessage, TResponse>>();

        var pipeline = behaviors.Any()
            ? behaviors
                .Reverse()
                .Aggregate<IPipelineRequestBehavior<TMessage, TResponse>, PipelineBehaviorDelegate<TMessage, Task<TResponse>>>(
                    App,
                    (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
                ) : App;

        return ErrorReporting;

        Task<TResponse> App(object? _, TMessage msg, CancellationToken ct) => handler.Handle(msg, ct);

        Task<TResponse> ErrorReporting(object? routingKey, TMessage msg, CancellationToken ct)
        {
            return pipeline(routingKey, msg, ct).ContinueWith(
                tt =>
                {
                    logger.LogError(
                        tt.Exception,
                        "Error executing notification pipeline for message of type {MessageType}: {Message}",
                        typeof(TMessage).FullName, tt.Exception!.Message);

                    return default(TResponse);
                },
                ct,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default
            );
        }
    }
}
