using GenDI;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace NetMediate;

/// <summary>
/// Defines a contract for asynchronously dispatching or publishing notification messages to all registered handlers.
/// </summary>
/// <remarks>Implementations of this interface provide mechanisms for notifying multiple handlers about events or
/// messages. The order in which handlers are invoked is not guaranteed unless explicitly documented by the
/// implementation. All notification operations are asynchronous and support cancellation via a cancellation
/// token.
/// If you must implement your own, uses [<c>DecoratorFor&lt;INotifiable&gt;</c>] to intercept and do your own implementation.
/// </remarks>
[ServiceInjection(ServiceLifetime.Transient, ThreadIsolation = ThreadIsolationPolicy.None, RegistrationEmission = RegistrationEmissionStrategy.TryAdd, RegistrationMultiplicity = RegistrationMultiplicity.Single)]
public interface INotifiable
{
    /// <summary>
    /// Dispatches a notification message directly to the provided handlers, bypassing the pipeline.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <param name="key">An optional key to distinguish this notification from others of the same message type.</param>
    /// <param name="message">The notification message instance.</param>
    /// <param name="handlers">The resolved handlers to invoke.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Task"/> that completes when dispatch finishes.</returns>
    Task DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        ImmutableArray<INotificationHandler<TMessage>> handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull;
}
