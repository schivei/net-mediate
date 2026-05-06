using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NetMediate.Internals;

/// <summary>
/// Specialized pipeline executor for request handlers that also resolves
/// <see cref="IPipelineRequestBehavior{TMessage,TResponse}"/> registrations in addition
/// to <see cref="IPipelineBehavior{TMessage,TResult}"/>. This avoids the need for
/// MakeGenericType and is fully AOT-compatible.
/// </summary>
internal sealed class RequestPipelineExecutor<TMessage, TResponse>(IServiceProvider serviceProvider)
    where TMessage : notnull
{
    private static readonly ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<object, Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>>
    > s_pipelineCache = new();

    static RequestPipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp =>
        {
            if (s_pipelineCache.TryGetValue(sp, out var cache))
                cache.Clear();
            s_pipelineCache.Remove(sp);
        });

    public Task<TResponse> Handle(
        object? key,
        TMessage message,
        CancellationToken cancellationToken
    )
    {
        var perProvider = s_pipelineCache.GetValue(
            serviceProvider,
            _ => new ConcurrentDictionary<
                object,
                Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>
            >()
        );

        var lazy = perProvider.GetOrAdd(
            key ?? Extensions.DEFAULT_ROUTING_KEY,
            _ => new Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>(
                () => BuildPipeline(key, serviceProvider),
                LazyThreadSafetyMode.ExecutionAndPublication
            )
        );

        return lazy.Value(key, message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, Task<TResponse>> BuildPipeline(
        object? key,
        IServiceProvider sp
    )
    {
        var handler = sp.GetHandlers<
            IRequestHandler<TMessage, TResponse>,
            TMessage,
            Task<TResponse>
        >(key).Single();

        PipelineBehaviorDelegate<TMessage, Task<TResponse>> app =
            (_, msg, ct) => handler.Handle(msg, ct);

        var behaviorArray = sp.GetCachedBehaviors<IPipelineBehavior<TMessage, Task<TResponse>>>()
            .Concat(
                sp.GetCachedBehaviors<IPipelineRequestBehavior<TMessage, TResponse>>()
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
