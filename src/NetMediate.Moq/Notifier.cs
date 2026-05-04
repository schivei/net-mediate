using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.Internals;

namespace NetMediate.Moq;

public class Notifier : INotifiable
{
    private readonly Internals.Notifier _notifier;

    public Notifier(IServiceProvider serviceProvider)
    {
        var logger = serviceProvider.GetService<ILogger<Internals.Notifier>>();
        _notifier = new(serviceProvider, logger);
    }
    
    public async Task DispatchNotifications<TMessage>(TMessage message, INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default) where TMessage : notnull
    {
        foreach (var handler in handlers)
        {
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) where TMessage : notnull =>
        _notifier.Notify(message, cancellationToken);

    /// <inheritdoc />
    public Task Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
        _notifier.Notify(messages, cancellationToken);
}
