using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

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
    // Per-provider pre-compiled pipeline cache — see PipelineExecutor<,,> for the full rationale.
    private static readonly ConditionalWeakTable<IServiceProvider, Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>>
        s_pipelineCache = new();

    static RequestPipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp => s_pipelineCache.Remove(sp));

    public Task<TResponse> Handle(
        TMessage message,
        HandlerExecutionDelegate<IRequestHandler<TMessage, TResponse>, TMessage, Task<TResponse>> exec,
        CancellationToken cancellationToken)
    {
        var lazy = s_pipelineCache.GetValue(
            serviceProvider,
            sp => new Lazy<PipelineBehaviorDelegate<TMessage, Task<TResponse>>>(
                () => BuildPipeline(sp, exec),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value(message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, Task<TResponse>> BuildPipeline(
        IServiceProvider sp,
        HandlerExecutionDelegate<IRequestHandler<TMessage, TResponse>, TMessage, Task<TResponse>> exec)
    {
        // Resolve handlers directly from the provider — the pipeline is already cached per-provider,
        // so this runs only once per provider. Using direct resolution avoids cross-provider
        // contamination that would occur with a global static handler cache.
        var handlers = sp.GetServices<IRequestHandler<TMessage, TResponse>>().ToArray();

        // Single-handler fast path: requests always have exactly one handler; bypass exec.
        PipelineBehaviorDelegate<TMessage, Task<TResponse>> app = handlers.Length == 1
            ? (msg, ct) => handlers[0].Handle(msg, ct)
            : (msg, ct) => exec(msg, handlers, ct);

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
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));
    }
}
