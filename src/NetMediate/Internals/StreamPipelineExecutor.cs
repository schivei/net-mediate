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
    public IAsyncEnumerable<TResponse> Handle(
        TMessage message,
        HandlerExecutionDelegate<IStreamHandler<TMessage, TResponse>, TMessage, IAsyncEnumerable<TResponse>> exec,
        CancellationToken cancellationToken)
    {
        var handlers = serviceProvider.GetHandlers<IStreamHandler<TMessage, TResponse>, TMessage, IAsyncEnumerable<TResponse>>();

        PipelineBehaviorDelegate<TMessage, IAsyncEnumerable<TResponse>> app = App;

        // Combine IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>> and IPipelineStreamBehavior<TMessage, TResponse>
        // both AOT-safe (no MakeGenericType). Results are cached per type to avoid repeated DI enumeration.
        IEnumerable<IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>>> behaviors =
            serviceProvider.GetCachedBehaviors<IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>>>()
                .Concat(serviceProvider.GetCachedBehaviors<IPipelineStreamBehavior<TMessage, TResponse>>()
                    .Cast<IPipelineBehavior<TMessage, IAsyncEnumerable<TResponse>>>());

        var pipeline = behaviors
            .Reverse()
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));

        return pipeline(message, cancellationToken);

        IAsyncEnumerable<TResponse> App(TMessage msg, CancellationToken ct) =>
            exec(msg, handlers, ct);
    }
}
