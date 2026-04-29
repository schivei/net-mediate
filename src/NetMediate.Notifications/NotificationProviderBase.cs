namespace NetMediate.Notifications;

/// <summary>
/// Abstract base class for custom <see cref="INotificationProvider"/> implementations.
/// </summary>
/// <remarks>
/// Subclass this type when implementing a custom notification backend such as an external
/// message broker (RabbitMQ, Kafka, Azure Service Bus, Redis Streams) or a custom in-process
/// queue strategy.
/// Inject <see cref="INotificationDispatcher"/> into your consumer and call
/// <see cref="INotificationDispatcher.DispatchAsync{TMessage}"/> to invoke the registered
/// <see cref="INotificationHandler{TMessage}"/> implementations once a message is dequeued.
/// </remarks>
public abstract class NotificationProviderBase : INotificationProvider
{
    /// <inheritdoc/>
    public abstract ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken);
}
