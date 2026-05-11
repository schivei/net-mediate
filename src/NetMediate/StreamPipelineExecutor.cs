using Microsoft.Extensions.DependencyInjection;

namespace NetMediate;

/// <summary>
/// Provides an abstract base class for executing a pipeline that processes messages and returns asynchronous streams of
/// responses.
/// </summary>
/// <remarks>The pipeline is constructed using handlers and behaviors resolved from the provided service provider for
/// each invocation. This class is intended to be extended to implement custom pipeline execution logic for streaming
/// scenarios.</remarks>
/// <typeparam name="TMessage">The type of message to be processed by the pipeline. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of response produced by the pipeline.</typeparam>
/// <param name="serviceProvider">The service provider used to resolve pipeline handlers and behaviors.</param>
public sealed class StreamPipelineExecutor<TMessage, TResponse>(IServiceProvider serviceProvider) where TMessage : notnull
{
    // Cached pipeline for the common key-less path; built once on the first call.
    private PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>>? _noKeyPipeline;

    /// <summary>
    /// Invokes the pipeline for the specified message and returns an asynchronous stream of responses.
    /// </summary>
    /// <remarks>The pipeline is built lazily for each invocation. The returned stream may yield zero or more
    /// responses depending on the handler implementation.</remarks>
    /// <param name="key">An optional key used to identify the pipeline instance. If null, keyless handlers are used.</param>
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
        if (key is null)
        {
            // Fast path: reuse the cached pipeline for the key-less case.
            var pipeline = _noKeyPipeline ?? InitNoKeyPipeline(exec);
            return pipeline(null, message, cancellationToken);
        }

        return BuildPipeline(key, exec)(key, message, cancellationToken);
    }

    private PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> InitNoKeyPipeline(
        HandlerExecutionDelegate<IStreamHandler<TMessage, TResponse>, TMessage, IAsyncEnumerable<TResponse>> exec)
    {
        var built = BuildPipeline(null, exec);
        Interlocked.CompareExchange(ref _noKeyPipeline, built, null);
        return _noKeyPipeline!;
    }

    private IStreamHandler<TMessage, TResponse>[] ResolveKeyedHandlers(object key)
    {
        var registry = serviceProvider.GetService<KeyedHandlerRegistry<IStreamHandler<TMessage, TResponse>>>();
        if (registry is not null && registry.TryGetAll(key, serviceProvider, out var handlers) && handlers.Length > 0)
            return handlers;
        return [.. serviceProvider.GetServices<IStreamHandler<TMessage, TResponse>>()];
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
            ? [.. serviceProvider.GetServices<IStreamHandler<TMessage, TResponse>>()]
            : ResolveKeyedHandlers(key);

        var behaviors = serviceProvider.GetServices<IPipelineStreamBehavior<TMessage, TResponse>>().ToArray();

        PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> app =
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

        return pipeline;
    }
}
