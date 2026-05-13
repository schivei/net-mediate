using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate;

/// <summary>
/// Executes a request handler pipeline for a specified message and returns the response asynchronously.
/// </summary>
/// <remarks>The request pipeline is built lazily for each invocation using handlers and behaviors resolved from the
/// provided service provider.</remarks>
/// <typeparam name="TMessage">The type of the message to be handled by the request pipeline. Must not be null.</typeparam>
/// <typeparam name="TResponse">The type of the response returned by the request handler pipeline.</typeparam>
/// <param name="serviceProvider">The service provider used to resolve request handlers and pipeline behaviors.</param>
/// <param name="logger">The logger used to record errors and diagnostic information during pipeline execution.</param>
public sealed class RequestPipelineExecutor<TMessage, TResponse>(IServiceProvider serviceProvider, ILogger<RequestPipelineExecutor<TMessage, TResponse>> logger)
    where TMessage : notnull
{
    // Cached pipeline for the common key-less path; built once on the first call.
    private PipelineBehaviorDelegate<TMessage, Task<TResponse>>? _noKeyPipeline;

    /// <summary>
    /// Invokes the request handler pipeline for the specified message and returns the response asynchronously.
    /// </summary>
    /// <remarks>The pipeline is built lazily for each invocation using the handlers and behaviors available for the
    /// provided key.</remarks>
    /// <param name="key">An optional key used to identify the pipeline instance. If null, keyless handlers are used.</param>
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
        if (key is null)
        {
            // Fast path: reuse the cached pipeline for the key-less case.
            var pipeline = _noKeyPipeline ?? InitNoKeyPipeline();
            return pipeline(null, message, cancellationToken);
        }

        return BuildPipeline(key)(key, message, cancellationToken);
    }

    private PipelineBehaviorDelegate<TMessage, Task<TResponse>> InitNoKeyPipeline()
    {
        var built = BuildPipeline(null);
        Interlocked.CompareExchange(ref _noKeyPipeline, built, null);
        return _noKeyPipeline!;
    }

    private IRequestHandler<TMessage, TResponse>[] ResolveKeyedHandlers(object key)
    {
        var registry = serviceProvider.GetService<KeyedHandlerRegistry<IRequestHandler<TMessage, TResponse>>>();
        if (registry is not null && registry.TryGetAll(key, serviceProvider, out var handlers) && handlers.Length > 0)
            return handlers;
        return [.. serviceProvider.GetServices<IRequestHandler<TMessage, TResponse>>()];
    }

    private PipelineBehaviorDelegate<TMessage, Task<TResponse>> BuildPipeline(object? key)
    {
        var handlers = key is null
            ? [.. serviceProvider.GetServices<IRequestHandler<TMessage, TResponse>>()]
            : ResolveKeyedHandlers(key);

        var handler = handlers.Single();

        var behaviors = serviceProvider.GetServices<IPipelineRequestBehavior<TMessage, TResponse>>().ToArray();

        var pipeline = behaviors.Length == 0
            ? App
            : behaviors.AsEnumerable().Reverse()
                .Aggregate<IPipelineRequestBehavior<TMessage, TResponse>, PipelineBehaviorDelegate<TMessage, Task<TResponse>>>(
                    App,
                    (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
                );

        return ErrorReporting;

        Task<TResponse> App(object? _, TMessage msg, CancellationToken ct) => handler.Handle(msg, ct);

        Task<TResponse> ErrorReporting(object? routingKey, TMessage msg, CancellationToken ct)
        {
            var task = pipeline(routingKey, msg, ct);
            // Avoid async state-machine allocation on the hot success path.
            return task.IsCompletedSuccessfully ? task : AwaitAndCatch(task);

            // ContinueWith avoids async state-machine coverage gaps in newer SDK versions
            // (sequence points for method-close braces of async Task<T> methods are unreliable).
            Task<TResponse> AwaitAndCatch(Task<TResponse> t) =>
                t.ContinueWith(
                    completed =>
                    {
                        if (completed.IsFaulted)
                            LogFailure(completed.Exception!.GetBaseException());
                        return completed.GetAwaiter().GetResult();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default
                );
        }

        void LogFailure(Exception ex) =>
            logger.LogError(
                ex,
                "Error executing request pipeline for message of type {MessageType}: {Message}",
                typeof(TMessage).FullName,
                ex.Message
            );
    }
}
