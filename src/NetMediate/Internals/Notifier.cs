using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal sealed class Notifier(Configuration configuration, IServiceScopeFactory serviceScopeFactory) : INotifiable
{
    public ValueTask Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull, INotification =>
        configuration.ChannelWriter.WriteAsync(new Pack<TMessage>(message, DispatchNotifications), cancellationToken);

    public async ValueTask Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull, INotification =>
        await Task.WhenAll(messages.Select(async message => await Notify(message, cancellationToken))).ConfigureAwait(false);

    public async ValueTask DispatchNotifications<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull, INotification
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Dispatch");
        using var scope = serviceScopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        try
        {
            var pipeline = Mediator.MountPipeline<
               TMessage,
               ValueTask,
               NotificationHandlerDelegate<TMessage>,
               INotificationHandler<TMessage>,
               INotificationBehavior<TMessage>>(
               serviceProvider,
               (validations, handlers) => async (message, token) =>
               {
                   await Mediator.ValidateMessageAsync(message, validations, token).ConfigureAwait(false);
                   foreach (var handler in handlers)
                       await handler.Handle(message, token).ConfigureAwait(false);
               },
               (behavior, next) => (message, token) => behavior.Handle(message, next, token)
            );

            if (pipeline is null)
            {
                return;
            }

            await pipeline.Invoke(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordDispatch<TMessage>();
        }
    }
}
