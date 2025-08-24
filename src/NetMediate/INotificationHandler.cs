namespace NetMediate;

/// <summary>
/// Defines a handler for notification messages of type <typeparamref name="TMessage"/>.
/// </summary>
/// <typeparam name="TMessage">
/// The type of notification message to handle.
/// </typeparam>
public interface INotificationHandler<in TMessage> : IHandler
{
    /// <summary>
    /// Handles a notification message.
    /// </summary>
    /// <param name="notification">The notification message to handle.</param>
    /// <param name="cancellationToken">
    /// A token to observe while waiting for the task to complete.
    /// </param>
    /// <returns>
    /// A task that represents the asynchronous operation.
    /// </returns>
    Task Handle(TMessage notification, CancellationToken cancellationToken = default);
}
