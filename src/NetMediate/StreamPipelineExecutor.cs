using Microsoft.Extensions.DependencyInjection;

namespace NetMediate;

/// <summary>
/// Provides an abstract base class for executing a pipeline that processes messages and returns asynchronous streams of
/// responses.
/// </summary>
/// <remarks>The pipeline is constructed using handlers and behaviors resolved from the provided service provider.
/// Pipelines are cached per service provider and key to improve performance for repeated invocations. This class is
/// intended to be extended to implement custom pipeline execution logic for streaming scenarios.</remarks>
/// <typeparam name="TMessage">The type of message to be processed by the pipeline. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the pipeline.</typeparam>
/// <param name="serviceProvider">The service provider used to resolve pipeline handlers and behaviors.</param>
public sealed class StreamPipelineExecutor<TMessage, TResponse>(IServiceProvider serviceProvider) where TMessage : notnull
{
    /// <summary>
    /// Invokes the pipeline for the specified message and returns an asynchronous stream of responses.
    /// </summary>
    /// <remarks>The pipeline is cached per service provider and key to optimize repeated invocations. The
    /// returned stream may yield zero or more responses depending on the handler implementation.</remarks>
    /// <param name="key">An optional key used to identify the pipeline instance. If null, a default routing key is used.</param>
    /// <param name="message">The message to be processed by the pipeline.</param>
    /// <param name="exec">The delegate that executes the handler pipeline for the message.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
    /// <returns>An asynchronous stream containing the responses produced by the pipeline for the given message.</returns>
    public IAsyncEnumerable<TResponse> Handle(
        object? key,
        TMessage message,
        HandlerExecutionDelegate<
            IStreamHandler<TMessage, TResponse>,
            TMessage,
            IAsyncEnumerable<TResponse>
        > exec,
        CancellationToken cancellationToken
    )
    {
        var lazy = new Lazy<PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>>>(
            () => BuildPipeline(key, exec),
            LazyThreadSafetyMode.ExecutionAndPublication
        );

        return lazy.Value(key, message, cancellationToken);
    }

    private IStreamHandler<TMessage, TResponse>[] ResolveKeyedHandlers(object key)
    {
        var registry = serviceProvider.GetService<KeyedHandlerRegistry<IStreamHandler<TMessage, TResponse>>>();
        if (registry is not null && registry.TryGet(key, out var keyed) && keyed is not null)
            return [keyed];
        return serviceProvider.GetServices<IStreamHandler<TMessage, TResponse>>().ToArray();
    }

    private PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> BuildPipeline(
        object? key,
        HandlerExecutionDelegate<
            IStreamHandler<TMessage, TResponse>,
            TMessage,
            IAsyncEnumerable<TResponse>
        > exec
    )
    {
        var handlers = key is null
            ? serviceProvider.GetServices<IStreamHandler<TMessage, TResponse>>().ToArray()
            : ResolveKeyedHandlers(key);

        var behaviors = serviceProvider.GetServices<IPipelineStreamBehavior<TMessage, TResponse>>();

        PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> app =
            handlers.Length == 1
                ? (_, msg, ct) => handlers[0].Handle(msg, ct)
                : (routingKey, msg, ct) => exec(routingKey, msg, handlers, ct);

        var pipeline = behaviors.Any()
            ? behaviors
                .Reverse()
                .Aggregate(
                    app,
                    (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
                ) : app;

        return pipeline;
    }
}
