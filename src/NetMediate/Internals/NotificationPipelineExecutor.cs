using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

/// <summary>
/// Specialized pipeline executor for notification handlers.
/// Resolves both <see cref="IPipelineBehavior{TMessage, Task}"/> (two-parameter, e.g. command/request
/// cross-cutting behaviors) and <see cref="IPipelineBehavior{TMessage}"/> (one-parameter, e.g. notification
/// adapters and notification-specific resilience behaviors) from DI, without needing a runtime type switch.
/// Registered as a closed type by <see cref="IMediatorServiceBuilder.RegisterNotificationHandler{THandler,TMessage}()"/>
/// so that no open-generic <c>typeof()</c> resolver is needed.
/// </summary>
internal sealed class NotificationPipelineExecutor<TMessage>(IServiceProvider serviceProvider)
    where TMessage : notnull
{
    public Task Handle(
        TMessage message,
        HandlerExecutionDelegate<INotificationHandler<TMessage>, TMessage, Task> exec,
        CancellationToken cancellationToken)
    {
        var handlers = serviceProvider.GetHandlers<INotificationHandler<TMessage>, TMessage, Task>();

        PipelineBehaviorDelegate<TMessage, Task> app = App;

        // Combine two-param behaviors (IPipelineBehavior<TMessage, Task>) with
        // one-param behaviors (IPipelineBehavior<TMessage>, e.g. adapter and resilience
        // notification behaviors registered via typeof(IPipelineBehavior<>)).
        // Both are AOT-safe closed-type lookups — no MakeGenericType or typeof(TResult) switch.
        IEnumerable<IPipelineBehavior<TMessage, Task>> behaviors =
            serviceProvider.GetServices<IPipelineBehavior<TMessage, Task>>()
                .Concat(serviceProvider.GetServices<IPipelineBehavior<TMessage>>()
                    .Cast<IPipelineBehavior<TMessage, Task>>());

        var pipeline = behaviors
            .Reverse()
            .Aggregate(app, (current, behavior) => (msg, ct) =>
                behavior.Handle(msg, current, ct));

        return pipeline(message, cancellationToken);

        Task App(TMessage msg, CancellationToken ct) =>
            exec(msg, handlers, ct);
    }
}
