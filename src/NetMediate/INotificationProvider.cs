namespace NetMediate;

/// <summary>
/// Pluggable notification dispatch backend.
/// </summary>
/// <remarks>
/// <para>
/// By default NetMediate uses an inline provider that dispatches handlers directly on the
/// calling thread.  Replace it by calling
/// <see cref="IMediatorServiceBuilder.UseNotificationProvider{TProvider}"/> to route
/// notifications through an external message broker, a different in-process strategy, or
/// any custom queue provider.
/// </para>
/// <para>
/// For Channel-based background delivery, add the <c>NetMediate.InternalNotifier</c>
/// package and call <c>AddNetMediateInternalNotifier()</c>.
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
    /// <param name="cancellationToken">Propagated from the original <see cref="IMediator"/> Notify call.</param>
    ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken);
}
