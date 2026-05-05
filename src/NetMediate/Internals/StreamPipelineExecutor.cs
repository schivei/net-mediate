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
    // Per-provider pre-compiled pipeline cache — see PipelineExecutor<,,> for the full rationale.
    private static readonly ConditionalWeakTable<IServiceProvider, Lazy<PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>>>>
        s_pipelineCache = new();

    static StreamPipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp => s_pipelineCache.Remove(sp));

    public IAsyncEnumerable<TResponse> Handle(
        object? key,
        TMessage message,
        HandlerExecutionDelegate<IStreamHandler<TMessage, TResponse>, TMessage, IAsyncEnumerable<TResponse>> exec,
        CancellationToken cancellationToken)
    {
        var lazy = s_pipelineCache.GetValue(
            serviceProvider,
            sp => new Lazy<PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>>>(
                () => BuildPipeline(key, sp, exec),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value(key, message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> BuildPipeline(
        object? key,
        IServiceProvider sp,
        HandlerExecutionDelegate<IStreamHandler<TMessage, TResponse>, TMessage, IAsyncEnumerable<TResponse>> exec)
    {
        // Resolve handlers directly from the provider — the pipeline is already cached per-provider,
        // so this runs only once per provider. Using direct resolution avoids cross-provider
        // contamination that would occur with a global static handler cache.
        var handlers = sp.GetHandlers<IStreamHandler<TMessage, TResponse>, TMessage, IAsyncEnumerable<TResponse>>(key).ToArray();

        // Single-handler fast path: invoke the sole registered handler directly.
        PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> app = handlers.Length == 1
            ? (_, msg, ct) => handlers[0].Handle(msg, ct)
            : (_, msg, ct) => exec(_, msg, handlers, ct);

        // Combine IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>> and IPipelineStreamBehavior<TMessage, TResponse>
        // both AOT-safe (no MakeGenericType). Results are cached per type to avoid repeated DI enumeration.
        var behaviorArray = sp.GetCachedBehaviors<IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>>>()
            .Concat(sp.GetCachedBehaviors<IPipelineStreamBehavior<TMessage, TResponse>>()
                .Cast<IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>>>())
            .ToArray();

        if (behaviorArray.Length == 0)
            return app;

        // Explicit Enumerable.Reverse avoids ambiguity with MemoryExtensions.Reverse(Span<T>).
        return Enumerable.Reverse(behaviorArray)
            .Aggregate(app, (current, behavior) => (key, msg, ct) =>
                behavior.Handle(key, msg, current, ct));
    }
}
