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
    private static readonly ConditionalWeakTable<IServiceProvider, ConcurrentDictionary<object, Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>>>
        s_pipelineCache = new();

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
        HandlerExecutionDelegate<IRequestHandler<TMessage, TResponse>, TMessage, Task<TResponse>> exec,
        CancellationToken cancellationToken)
    {
        var perProvider = s_pipelineCache.GetValue(
            serviceProvider,
            _ => new ConcurrentDictionary<object, Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>>());

        var lazy = perProvider.GetOrAdd(
            key ?? Extensions.DEFAULT_ROUTING_KEY,
            _ => new Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>(
                () => BuildPipeline(key, serviceProvider, exec),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value(key, message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, Task<TResponse>> BuildPipeline(
        object? key,
        IServiceProvider sp,
        HandlerExecutionDelegate<IRequestHandler<TMessage, TResponse>, TMessage, Task<TResponse>> exec)
    {
        // Resolve handlers directly from the provider — the pipeline is already cached per-provider,
        // so this runs only once per provider. Using direct resolution avoids cross-provider
        // contamination that would occur with a global static handler cache.
        var handlers = sp.GetHandlers<IRequestHandler<TMessage, TResponse>, TMessage, Task<TResponse>>(key).ToArray();

        // Single-handler fast path: requests always have exactly one handler; bypass exec.
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> app = handlers.Length == 1
            ? (_, msg, ct) => handlers[0].Handle(msg, ct)
            : (key, msg, ct) => exec(key, msg, handlers, ct);

        // Combine IPipelineBehavior<TMessage, Task<TResponse>> and IPipelineRequestBehavior<TMessage, TResponse>
        // both AOT-safe (no MakeGenericType). Results are cached per type to avoid repeated DI enumeration.
        var behaviorArray = sp.GetCachedBehaviors<IPipelineBehavior<TMessage, Task<TResponse>>>()
            .Concat(sp.GetCachedBehaviors<IPipelineRequestBehavior<TMessage, TResponse>>()
                .Cast<IPipelineBehavior<TMessage, Task<TResponse>>>())
            .ToArray();

        if (behaviorArray.Length == 0)
            return app;

        // Explicit Enumerable.Reverse avoids ambiguity with MemoryExtensions.Reverse(Span<T>).
        return Enumerable.Reverse(behaviorArray)
            .Aggregate(app, (current, behavior) => (key, msg, ct) =>
                behavior.Handle(key, msg, current, ct));
    }
}
