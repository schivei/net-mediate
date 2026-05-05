using System.Runtime.CompilerServices;

namespace NetMediate.Internals;

/// <summary>
/// Specialized pipeline executor for notification handlers.
/// Resolves <see cref="IPipelineBehavior{TMessage, Task}"/> (two-parameter),
/// <see cref="IPipelineBehavior{TMessage}"/> (one-parameter), and
/// <see cref="IPipelineNotificationBehavior{TMessage}"/> (notification shorthand) from DI,
/// without needing a runtime type switch.
/// Registered as a closed type by <c>RegisterNotificationHandler&lt;THandler, TMessage&gt;(...)</c>
/// so that no open-generic <c>typeof()</c> resolver is needed.
/// </summary>
internal sealed class NotificationPipelineExecutor<TMessage>(IServiceProvider serviceProvider)
    where TMessage : notnull
{
    // Per-provider pre-compiled pipeline cache — see PipelineExecutor<,,> for the full rationale.
    private static readonly ConditionalWeakTable<IServiceProvider, Lazy<PipelineBehaviorDelegate<TMessage, Task>>>
        s_pipelineCache = new();

    static NotificationPipelineExecutor() =>
        Extensions.RegisterPipelineCacheClearing(sp => s_pipelineCache.Remove(sp));

    public Task Handle(
        object? key,
        TMessage message,
        HandlerExecutionDelegate<INotificationHandler<TMessage>, TMessage, Task> exec,
        CancellationToken cancellationToken)
    {
        var lazy = s_pipelineCache.GetValue(
            serviceProvider,
            sp => new Lazy<PipelineBehaviorDelegate<TMessage, Task>>(
                () => BuildPipeline(key, sp, exec),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazy.Value(key, message, cancellationToken);
    }

    private static PipelineBehaviorDelegate<TMessage, Task> BuildPipeline(
        object? key,
        IServiceProvider sp,
        HandlerExecutionDelegate<INotificationHandler<TMessage>, TMessage, Task> exec)
    {
        // Resolve handlers directly from the provider — the pipeline is already cached per-provider,
        // so this runs only once per provider. Using direct resolution avoids cross-provider
        // contamination that would occur with a global static handler cache.
        var handlers = sp.GetHandlers<INotificationHandler<TMessage>, TMessage, Task>(key).ToArray();

        Task app(object? key, TMessage msg, CancellationToken ct) => exec(key, msg, handlers, ct);

        // Combine three behavior types — all AOT-safe closed-type lookups, no MakeGenericType:
        //   1. IPipelineBehavior<TMessage, Task>          — general two-param behaviors
        //   2. IPipelineBehavior<TMessage>                — one-param notification/adapter behaviors
        //   3. IPipelineNotificationBehavior<TMessage>    — notification-specific shorthand
        // Results are cached per provider to avoid repeated DI enumeration.
        var behaviorArray = sp.GetCachedBehaviors<IPipelineBehavior<TMessage, Task>>()
            .Concat(sp.GetCachedBehaviors<IPipelineBehavior<TMessage>>()
                .Cast<IPipelineBehavior<TMessage, Task>>())
            .Concat(sp.GetCachedBehaviors<IPipelineNotificationBehavior<TMessage>>()
                .Cast<IPipelineBehavior<TMessage, Task>>())
            .ToArray();

        if (behaviorArray.Length == 0)
            return app;

        // Explicit Enumerable.Reverse avoids ambiguity with MemoryExtensions.Reverse(Span<T>).
        return Enumerable.Reverse(behaviorArray)
            .Aggregate((PipelineBehaviorDelegate<TMessage, Task>)app, (current, behavior) => (key, msg, ct) =>
                behavior.Handle(key, msg, current, ct));
    }
}
