using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal class PipelineExecutor<TMessage, TResult, THandler>(IServiceProvider serviceProvider) // NOSONAR S2436
    where TMessage : notnull
    where TResult : notnull
    where THandler : class, IHandler<TMessage, TResult>
{
    // Per-provider cache of the pre-compiled pipeline delegate.
    // The static field is unique per closed generic instantiation, so different message/result/
    // handler type combinations each maintain their own isolated cache without key collisions.
    private static readonly ConditionalWeakTable<IServiceProvider, Lazy<PipelineBehaviorDelegate<TMessage, TResult>>>
        s_pipelineCache = new();

    // Register the cache-clearing action once per closed-generic instantiation (static ctor).
    // ClearCache(IServiceProvider) in Extensions will invoke this to invalidate the pre-compiled
    // chain for a specific provider (used by test isolation helpers).
    static PipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp => s_pipelineCache.Remove(sp));

    public TResult Handle(TMessage message, HandlerExecutionDelegate<THandler, TMessage, TResult> exec, CancellationToken cancellationToken)
    {
        // Build the pipeline delegate once per provider and reuse it on every subsequent call,
        // eliminating the per-call Reverse/Aggregate/closure allocation.
        var lazy = s_pipelineCache.GetValue(
            serviceProvider,
            sp => new Lazy<PipelineBehaviorDelegate<TMessage, TResult>>(
                () => BuildPipeline(sp, exec),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value(message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, TResult> BuildPipeline(
        IServiceProvider sp,
        HandlerExecutionDelegate<THandler, TMessage, TResult> exec)
    {
        // Resolve handlers directly from the provider — the pipeline is already cached per-provider,
        // so this runs only once per provider. Using direct resolution avoids cross-provider
        // contamination that would occur with a global static handler cache.
        var handlers = sp.GetServices<THandler>().ToArray();
        var behaviors = sp.GetCachedBehaviors<IPipelineBehavior<TMessage, TResult>>();

        // Single-handler fast path: bypass the exec delegate's foreach loop and invoke the
        // sole registered handler directly.  Applies regardless of whether behaviors are present.
        PipelineBehaviorDelegate<TMessage, TResult> app = handlers.Length == 1
            ? (msg, ct) => handlers[0].Handle(msg, ct)
            : (msg, ct) => exec(msg, handlers, ct);

        if (behaviors.Length == 0)
            return app;

        // Reverse once here (at build time, not per-message), then Aggregate to compose the
        // behavior chain.  Both allocations happen only on first call per provider.
        // Explicit Enumerable.Reverse avoids ambiguity with MemoryExtensions.Reverse(Span<T>)
        // when targeting netstandard2.0/2.1 (T[] converts implicitly to Span<T>).
        return Enumerable.Reverse(behaviors)
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));
    }
}
