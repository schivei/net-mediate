namespace NetMediate;

/// <summary>
/// Defines a pipeline behavior for notification messages.
/// </summary>
/// <typeparam name="TMessage">The notification message type.</typeparam>
public interface INotificationBehavior<in TMessage>
{
    /// <summary>
    /// Handles a notification before and/or after invoking the next delegate in the pipeline.
    /// </summary>
    /// <param name="message">The notification message.</param>
    /// <param name="next">The next delegate in the notification pipeline.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the task to complete.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task Handle(
        TMessage message,
        NotificationHandlerDelegate next,
        CancellationToken cancellationToken = default
    );
}
