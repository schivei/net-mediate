using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NetMediate.Internals;

/// <summary>
/// Specialized pipeline executor for stream handlers that also resolves
/// <see cref="IPipelineStreamBehavior{TMessage,TResponse}"/> registrations in addition
/// to <see cref="IPipelineBehavior{TMessage,TResult}"/>. This avoids the need for
/// MakeGenericType and is fully AOT-compatible.
/// </summary>
internal sealed class StreamPipelineExecutor<TMessage, TResponse>(IServiceProvider serviceProvider)
    where TMessage : notnull
{
    private static readonly ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<
            object,
            Lazy<PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>>>
        >
    > s_pipelineCache = new();

    static StreamPipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp =>
        {
            if (s_pipelineCache.TryGetValue(sp, out var cache))
                cache.Clear();
            s_pipelineCache.Remove(sp);
        });

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
        var perProvider = s_pipelineCache.GetValue(
            serviceProvider,
            _ => new ConcurrentDictionary<
                object,
                Lazy<PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>>>
            >()
        );

        var lazy = perProvider.GetOrAdd(
            key ?? Extensions.DEFAULT_ROUTING_KEY,
            _ => new Lazy<PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>>>(
                () => BuildPipeline(key, serviceProvider, exec),
                LazyThreadSafetyMode.ExecutionAndPublication
            )
        );

        return lazy.Value(key, message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> BuildPipeline(
        object? key,
        IServiceProvider sp,
        HandlerExecutionDelegate<
            IStreamHandler<TMessage, TResponse>,
            TMessage,
            IAsyncEnumerable<TResponse>
        > exec
    )
    {
        var handlers = sp.GetHandlers<
            IStreamHandler<TMessage, TResponse>,
            TMessage,
            IAsyncEnumerable<TResponse>
        >(key).ToArray();

        PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> app =
            handlers.Length == 1
                ? (_, msg, ct) => handlers[0].Handle(msg, ct)
                : (routingKey, msg, ct) => exec(routingKey, msg, handlers, ct);

        var behaviorArray = sp.GetCachedBehaviors<
            IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>>
        >()
            .Concat(
                sp.GetCachedBehaviors<IPipelineStreamBehavior<TMessage, TResponse>>()
            )
            .ToArray();

        if (behaviorArray.Length == 0)
            return app;

        return behaviorArray
            .Reverse()
            .Aggregate(
                app,
                (current, behavior) => (routingKey, msg, ct) => behavior.Handle(routingKey, msg, current, ct)
            );
    }
}
