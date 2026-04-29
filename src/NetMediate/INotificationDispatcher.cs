namespace NetMediate;

/// <summary>
/// Dispatches a notification message directly to all registered
/// <see cref="INotificationHandler{TMessage}"/> implementations, bypassing any
/// <see cref="INotificationProvider"/> queue.
/// </summary>
/// <remarks>
/// Inject this service into a custom queue consumer (e.g. a RabbitMQ consumer, Kafka consumer,
/// Hangfire job) to invoke the handlers once a message has been dequeued.
/// </remarks>
public interface INotificationDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="message"/> to every registered
    /// <see cref="INotificationHandler{TMessage}"/> and awaits completion.
    /// Exceptions from handlers propagate as <see cref="System.AggregateException"/>.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default);
}
