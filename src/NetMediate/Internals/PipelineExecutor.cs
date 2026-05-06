using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NetMediate.Internals;

#pragma warning disable S2436
internal class PipelineExecutor<TMessage, TResult, THandler>(IServiceProvider serviceProvider)
    where TMessage : notnull
    where TResult : notnull
    where THandler : class, IHandler<TMessage, TResult>
{
    private static readonly ConditionalWeakTable<
        IServiceProvider,
        ConcurrentDictionary<object, Lazy<PipelineBehaviorDelegate<TMessage, TResult>>>
    > s_pipelineCache = new();

    static PipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp =>
        {
            if (s_pipelineCache.TryGetValue(sp, out var cache))
                cache.Clear();
            s_pipelineCache.Remove(sp);
        });

    public TResult Handle(
        object? key,
        TMessage message,
        HandlerExecutionDelegate<THandler, TMessage, TResult> exec,
        CancellationToken cancellationToken
    )
    {
        var perProvider = s_pipelineCache.GetValue(
            serviceProvider,
            _ => new ConcurrentDictionary<
                object,
                Lazy<PipelineBehaviorDelegate<TMessage, TResult>>
            >()
        );

        var lazy = perProvider.GetOrAdd(
            key ?? Extensions.DEFAULT_ROUTING_KEY,
            _ => new Lazy<PipelineBehaviorDelegate<TMessage, TResult>>(
                () => BuildPipeline(key, serviceProvider, exec),
                LazyThreadSafetyMode.ExecutionAndPublication
            )
        );

        return lazy.Value(key, message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, TResult> BuildPipeline(
        object? key,
        IServiceProvider sp,
        HandlerExecutionDelegate<THandler, TMessage, TResult> exec
    )
    {
        var handlers = sp.GetHandlers<THandler, TMessage, TResult>(key).ToArray();
        var behaviors = sp.GetCachedBehaviors<IPipelineBehavior<TMessage, TResult>>();

        PipelineBehaviorDelegate<TMessage, TResult> app =
            handlers.Length == 1
                ? (_, msg, ct) => handlers[0].Handle(msg, ct)
                : (key, msg, ct) => exec(key, msg, handlers, ct);

        if (behaviors.Length == 0)
            return app;

        return Enumerable
            .Reverse(behaviors)
            .Aggregate(
                app,
                (current, behavior) => (key, msg, ct) => behavior.Handle(key, msg, current, ct)
            );
    }
}
#pragma warning restore S2436
