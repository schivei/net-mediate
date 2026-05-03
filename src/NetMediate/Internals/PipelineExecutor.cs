using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal class PipelineExecutor<TMessage, TResult, THandler>(IServiceProvider serviceProvider) // NOSONAR S2436
    where TMessage : notnull
    where TResult : notnull
    where THandler : class, IHandler<TMessage, TResult>
{
    public TResult Handle(TMessage message, HandlerExecutionDelegate<THandler, TMessage, TResult> exec, CancellationToken cancellationToken)
    {
        var handlers = serviceProvider.GetHandlers<THandler, TMessage, TResult>();

        PipelineBehaviorDelegate<TMessage, TResult> app = App;

        // Resolve behaviors — closed-type lookup, AOT-safe, no MakeGenericType or typeof(TResult) switch.
        // Notification pipelines use NotificationPipelineExecutor<TMessage> which also resolves
        // IPipelineBehavior<TMessage> (one-param) registrations. This executor is for commands only.
        IEnumerable<IPipelineBehavior<TMessage, TResult>> behaviors =
            serviceProvider.GetServices<IPipelineBehavior<TMessage, TResult>>();

        var pipeline = behaviors
            .Reverse()
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));

        return pipeline(message, cancellationToken);

        TResult App(TMessage msg, CancellationToken ct) =>
            exec(msg, handlers, ct);
    }
}