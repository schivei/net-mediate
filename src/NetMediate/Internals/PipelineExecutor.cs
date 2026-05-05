using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NetMediate.Internals;

internal class PipelineExecutor<TMessage, TResult, THandler>(IServiceProvider serviceProvider) // NOSONAR S2436
    where TMessage : notnull
    where TResult : notnull
    where THandler : class, IHandler<TMessage, TResult>
{
    // Sentinel used as dictionary key when the routing key is null, because
    // ConcurrentDictionary does not support null keys.
    private static readonly object s_nullKey = new();

    // Per-provider, per-routing-key cache of the pre-compiled pipeline delegate.
    // The outer ConditionalWeakTable is keyed by IServiceProvider (so entries are released when
    // the provider is GC'd). The inner ConcurrentDictionary maps routing key → compiled delegate,
    // ensuring that different routing keys produce different handler selections without
    // contaminating each other or baking the first key into every subsequent dispatch.
    private static readonly ConditionalWeakTable<IServiceProvider, ConcurrentDictionary<object, Lazy<PipelineBehaviorDelegate<TMessage, TResult>>>>
        s_pipelineCache = new();

    // Register the cache-clearing action once per closed-generic instantiation (static ctor).
    // ClearCache(IServiceProvider) in Extensions will invoke this to invalidate the pre-compiled
    // chain for a specific provider (used by test isolation helpers).
    static PipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp =>
        {
            if (s_pipelineCache.TryGetValue(sp, out var cache))
                cache.Clear();
            s_pipelineCache.Remove(sp);
        });

    public TResult Handle(object? key, TMessage message, HandlerExecutionDelegate<THandler, TMessage, TResult> exec, CancellationToken cancellationToken)
    {
        // Build the pipeline delegate once per (provider, routing-key) pair and reuse it on
        // every subsequent call, eliminating the per-call Reverse/Aggregate/closure allocation.
        var perProvider = s_pipelineCache.GetValue(
            serviceProvider,
            _ => new ConcurrentDictionary<object, Lazy<PipelineBehaviorDelegate<TMessage, TResult>>>());

        var dictKey = key ?? s_nullKey;
        var lazy = perProvider.GetOrAdd(
            dictKey,
            _ => new Lazy<PipelineBehaviorDelegate<TMessage, TResult>>(
                () => BuildPipeline(key, serviceProvider, exec),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value(key, message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, TResult> BuildPipeline(
        object? key,
        IServiceProvider sp,
        HandlerExecutionDelegate<THandler, TMessage, TResult> exec)
    {
        // Resolve handlers directly from the provider — the pipeline is already cached per-provider,
        // so this runs only once per provider. Using direct resolution avoids cross-provider
        // contamination that would occur with a global static handler cache.
        var handlers = sp.GetHandlers<THandler, TMessage, TResult>(key).ToArray();
        var behaviors = sp.GetCachedBehaviors<IPipelineBehavior<TMessage, TResult>>();

        // Single-handler fast path: bypass the exec delegate's foreach loop and invoke the
        // sole registered handler directly.  Applies regardless of whether behaviors are present.
        PipelineBehaviorDelegate<TMessage, TResult> app = handlers.Length == 1
            ? (_, msg, ct) => handlers[0].Handle(msg, ct)
            : (key, msg, ct) => exec(key, msg, handlers, ct);

        if (behaviors.Length == 0)
            return app;

        // Reverse once here (at build time, not per-message), then Aggregate to compose the
        // behavior chain.  Both allocations happen only on first call per provider.
        // Explicit Enumerable.Reverse avoids ambiguity with MemoryExtensions.Reverse(Span<T>)
        // when targeting netstandard2.0/2.1 (T[] converts implicitly to Span<T>).
        return Enumerable.Reverse(behaviors)
            .Aggregate(app, (current, behavior) => (key, msg, ct) =>
                behavior.Handle(key, msg, current, ct));
    }
}
