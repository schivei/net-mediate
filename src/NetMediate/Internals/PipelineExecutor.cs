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
        
        var pipeline = serviceProvider
            .GetServices<IPipelineBehavior<TMessage, TResult>>()
            .Reverse()
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));

        return pipeline(message, cancellationToken);

        TResult App(TMessage msg, CancellationToken ct) =>
            exec(msg, handlers, ct);
    }
}