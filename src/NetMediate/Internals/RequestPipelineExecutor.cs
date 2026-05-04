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
    public Task<TResponse> Handle(
        TMessage message,
        HandlerExecutionDelegate<IRequestHandler<TMessage, TResponse>, TMessage, Task<TResponse>> exec,
        CancellationToken cancellationToken)
    {
        var handlers = serviceProvider.GetHandlers<IRequestHandler<TMessage, TResponse>, TMessage, Task<TResponse>>();

        PipelineBehaviorDelegate<TMessage, Task<TResponse>> app = App;

        // Combine IPipelineBehavior<TMessage, Task<TResponse>> and IPipelineRequestBehavior<TMessage, TResponse>
        // both AOT-safe (no MakeGenericType). Results are cached per type to avoid repeated DI enumeration.
        IEnumerable<IPipelineBehavior<TMessage, Task<TResponse>>> behaviors =
            serviceProvider.GetCachedBehaviors<IPipelineBehavior<TMessage, Task<TResponse>>>()
                .Concat(serviceProvider.GetCachedBehaviors<IPipelineRequestBehavior<TMessage, TResponse>>()
                    .Cast<IPipelineBehavior<TMessage, Task<TResponse>>>());

        var pipeline = behaviors
            .Reverse()
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));

        return pipeline(message, cancellationToken);

        Task<TResponse> App(TMessage msg, CancellationToken ct) =>
            exec(msg, handlers, ct);
    }
}
