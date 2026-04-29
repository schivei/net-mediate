namespace NetMediate;

/// <summary>
/// Pluggable notification dispatch backend.
/// </summary>
/// <remarks>
/// <para>
/// By default NetMediate uses an in-process <see cref="System.Threading.Channels.Channel{T}"/>
/// drained by a hosted background worker (<c>NotificationWorker</c>).  Implement this interface
/// and register it with
/// <see cref="IMediatorServiceBuilder.UseNotificationProvider{TProvider}"/> to replace that
/// pipeline with your own backend — an external message broker (RabbitMQ, Kafka, Azure Service
/// Bus, Redis Streams, …), a different in-process strategy (inline sequential, parallel
/// <see cref="System.Threading.Tasks.Task.WhenAll"/>), or any custom queue provider.
/// </para>
/// <para>
/// When a custom provider is registered the built-in background worker is <b>not</b> started,
/// so there is no idle <see cref="System.Threading.Tasks.Task"/> running in the host.
/// The provider is solely responsible for delivering the notification to its handlers.
/// To invoke the registered <see cref="INotificationHandler{TMessage}"/> implementations use
/// the <see cref="INotificationDispatcher"/> service, which is always available from DI.
/// </para>
/// </remarks>
public interface INotificationProvider
{
    /// <summary>
    /// Receives a notification for delivery and enqueues or dispatches it according to the
    /// provider's strategy.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="message">The notification payload.</param>
    /// <param name="onError">
    /// Delegate invoked when a handler throws.  Receives the handler type, the message, and the
    /// exception.  May be <see langword="null"/> or the no-op default.
    /// </param>
    /// <param name="cancellationToken">Propagated from the original <see cref="IMediator.Notify{TMessage}"/> call.</param>
    /// <returns>
    /// A <see cref="System.Threading.Tasks.ValueTask"/> that completes once the notification has been
    /// accepted for delivery (not necessarily once all handlers have finished).
    /// </returns>
    ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        NotificationErrorDelegate<TMessage> onError,
        CancellationToken cancellationToken);
}
