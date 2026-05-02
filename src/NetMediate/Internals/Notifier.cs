using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal class Notifier(IServiceProvider serviceProvider, ILogger<Notifier> logger) : INotifiable
{
    public virtual Task DispatchNotifications<TMessage>(TMessage message, INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        Task.WhenAll(handlers.Select(handler => handler.Handle(message, cancellationToken)))
            .ContinueWith(t =>
            {
                if (!t.IsFaulted) return;
                
                logger.LogError(t.Exception, "{Message}", t.Exception!.Message);
            }, TaskContinuationOptions.OnlyOnFaulted)
            .ConfigureAwait(false);
        
        return Task.CompletedTask;
    }

    public Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        var pipeline = serviceProvider
            .GetRequiredService<PipelineExecutor<TMessage, Task, INotificationHandler<TMessage>>>();
        
        return pipeline.Handle(message, DispatchNotifications, cancellationToken);
    }

    public Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        return Task.WhenAll(messages.Select(message => Notify(message, cancellationToken)));
    }
}
