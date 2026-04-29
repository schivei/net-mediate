namespace NetMediate;

/// <summary>
/// Dispatches a notification message directly to all registered
/// <see cref="INotificationHandler{TMessage}"/> implementations, bypassing any
/// <see cref="INotificationProvider"/> queue.
/// </summary>
/// <remarks>
/// <para>
/// Inject this service into your custom queue consumer (e.g. a RabbitMQ consumer, a Kafka
/// consumer, a Hangfire job) to invoke the handlers once a message has been dequeued.
/// </para>
/// <para>
/// The built-in <c>NotificationWorker</c> uses this service internally; it is always
/// registered in DI regardless of which <see cref="INotificationProvider"/> is active.
/// </para>
/// </remarks>
public interface INotificationDispatcher
{
    /// <summary>
    /// Dispatches <paramref name="message"/> to every registered
    /// <see cref="INotificationHandler{TMessage}"/> and awaits completion.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification payload.</param>
    /// <param name="onError">
    /// Delegate invoked when a handler throws.  Receives the handler type, the message, and the
    /// exception.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchAsync<TMessage>(
        TMessage message,
        NotificationErrorDelegate<TMessage> onError,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches <paramref name="message"/> to every registered
    /// <see cref="INotificationHandler{TMessage}"/> without an error callback.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DispatchAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default)
#if NETSTANDARD2_0
        ;
#else
        => DispatchAsync(message, (_, _, _) => Task.CompletedTask, cancellationToken);
#endif
}
