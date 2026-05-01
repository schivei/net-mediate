using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;

namespace NetMediate.Moq;

public sealed class Notifier(IServiceScopeFactory serviceScopeFactory) : INotifiable
{
    public ValueTask Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull, INotification =>
        DispatchNotifications(message, cancellationToken);

    public async ValueTask Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull, INotification =>
        await Task.WhenAll(messages.Select(async message => await DispatchNotifications(message, cancellationToken))).ConfigureAwait(false);

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
                   foreach (var validation in validations)
                   {
                       await validation.ValidateAsync(message, token);
                   }

                   foreach (var handler in handlers)
                   {
                       await handler.Handle(message, token);
                   }
               },
               (behavior, next) => (message, token) => behavior.Handle(message, next, token)
            );

            await pipeline.Invoke(message, cancellationToken);
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
