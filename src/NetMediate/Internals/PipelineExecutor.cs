using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal class PipelineExecutor<TMessage, TResult, THandler>(IServiceProvider serviceProvider)
    where TMessage : notnull
    where TResult : notnull
    where THandler : class, IHandler<TMessage, TResult>
{
    public TResult Handle(TMessage message, HandlerExecutionDelegate<THandler, TMessage, TResult> exec, CancellationToken cancellationToken)
    {
        var handlers = serviceProvider.GetHandlers<THandler, TMessage, TResult>();

        PipelineBehaviorDelegate<TMessage, TResult> app = App;

        // Resolve behaviors using a switch on TResult to stay AOT-safe (no MakeGenericType).
        // Notification/command pipelines (TResult=Task) also pick up one-param IPipelineBehavior<TMessage>
        // registrations (e.g. notification adapters, resilience notification behaviors).
        // All other pipelines (requests, streams) use the standard two-param IPipelineBehavior<TMessage, TResult>.
        IEnumerable<IPipelineBehavior<TMessage, TResult>> behaviors = typeof(TResult) switch
        {
            var t when t == typeof(Task) =>
                serviceProvider.GetServices<IPipelineBehavior<TMessage, Task>>()
                    .Concat(serviceProvider.GetServices<IPipelineBehavior<TMessage>>()
                        .Cast<IPipelineBehavior<TMessage, Task>>())
                    .Cast<IPipelineBehavior<TMessage, TResult>>(),
            _ => serviceProvider.GetServices<IPipelineBehavior<TMessage, TResult>>()
        };

        var pipeline = behaviors
            .Reverse()
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));

        return pipeline(message, cancellationToken);

        TResult App(TMessage msg, CancellationToken ct) =>
            exec(msg, handlers, ct);
    }
}