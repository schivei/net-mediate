namespace NetMediate.Adapters;

/// <summary>
/// Defines a contract for forwarding a notification message to an external queue, stream, or messaging system.
/// </summary>
/// <remarks>
/// <para>
/// Implement this interface to integrate NetMediate notifications with your preferred messaging infrastructure
/// (e.g., RabbitMQ, Kafka, Azure Service Bus, AWS SNS/SQS, NATS, Redis Streams, or any other system).
/// </para>
/// <para>
/// Adapters are invoked from <see cref="NotificationAdapterBehavior{TMessage}"/> which sits in the NetMediate
/// notification pipeline. Every adapter registered for a given message type is called in registration order
/// after the core notification handlers run.
/// </para>
/// <para>
/// An adapter should be idempotent where possible; the behavior can be retried if an exception is thrown
/// (depending on the resilience behaviors configured in the pipeline).
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The notification message type this adapter handles.</typeparam>
public interface INotificationAdapter<TMessage> where TMessage : notnull
{
    /// <summary>
    /// Forwards the notification to an external destination.
    /// </summary>
    /// <param name="envelope">
    /// The standard envelope containing the message payload, a unique message identifier, the message type name,
    /// and the UTC creation timestamp.
    /// </param>
    /// <param name="cancellationToken">A cancellation token for the operation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous forward operation.</returns>
    Task ForwardAsync(AdapterEnvelope<TMessage> envelope, CancellationToken cancellationToken = default);
}
