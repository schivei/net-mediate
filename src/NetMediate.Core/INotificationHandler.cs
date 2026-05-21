using GenDI;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate;

/// <summary>
/// Defines a handler for notification messages that do not return a result.
/// </summary>
/// <remarks>Notification handlers are typically used to process events or signals that may be handled by zero or
/// more handlers. Unlike request handlers, notification handlers do not return a value to the sender.</remarks>
/// <typeparam name="TMessage">The type of notification message to handle. Must not be null.</typeparam>
[ServiceInjection(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None, RegistrationMultiplicity = RegistrationMultiplicity.Multiple)]
public interface INotificationHandler<in TMessage> : IHandler<TMessage, ValueTask>
    where TMessage : notnull;
