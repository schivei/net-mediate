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

        IEnumerable<IPipelineBehavior<TMessage, TResult>> behaviors =
            serviceProvider.GetServices<IPipelineBehavior<TMessage, TResult>>();

        // When TResult is Task (notification/command pipelines), also include behaviors
        // registered under the one-parameter IPipelineBehavior<TMessage> service type
        // (e.g. notification adapters and notification-scoped resilience behaviors).
        if (typeof(TResult) == typeof(Task))
        {
            var oneParamBehaviors = serviceProvider
                .GetServices(typeof(IPipelineBehavior<>).MakeGenericType(typeof(TMessage)))
                .Cast<IPipelineBehavior<TMessage, TResult>>();
            behaviors = behaviors.Concat(oneParamBehaviors);
        }
        // When TResult is Task<TResponse>, also include IPipelineRequestBehavior<TMessage, TResponse>.
        else if (typeof(TResult).IsGenericType &&
                 typeof(TResult).GetGenericTypeDefinition() == typeof(Task<>))
        {
            var responseType = typeof(TResult).GetGenericArguments()[0];
            var requestBehaviorType = typeof(IPipelineRequestBehavior<,>)
                .MakeGenericType(typeof(TMessage), responseType);
            var requestBehaviors = serviceProvider
                .GetServices(requestBehaviorType)
                .Cast<IPipelineBehavior<TMessage, TResult>>();
            behaviors = behaviors.Concat(requestBehaviors);
        }
        // When TResult is IAsyncEnumerable<TResponse>, also include IPipelineStreamBehavior<TMessage, TResponse>.
        else if (typeof(TResult).IsGenericType &&
                 typeof(TResult).GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>))
        {
            var responseType = typeof(TResult).GetGenericArguments()[0];
            var streamBehaviorType = typeof(IPipelineStreamBehavior<,>)
                .MakeGenericType(typeof(TMessage), responseType);
            var streamBehaviors = serviceProvider
                .GetServices(streamBehaviorType)
                .Cast<IPipelineBehavior<TMessage, TResult>>();
            behaviors = behaviors.Concat(streamBehaviors);
        }

        var pipeline = behaviors
            .Reverse()
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));

        return pipeline(message, cancellationToken);

        TResult App(TMessage msg, CancellationToken ct) =>
            exec(msg, handlers, ct);
    }
}