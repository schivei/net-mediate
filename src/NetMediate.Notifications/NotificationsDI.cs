using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.Notifications;

/// <summary>
/// Extension methods for registering notification provider infrastructure.
/// </summary>
public static class NotificationsDI
{
    /// <summary>
    /// Replaces the default notification provider with the supplied custom provider type.
    /// </summary>
    /// <typeparam name="TProvider">
    /// A concrete <see cref="INotificationProvider"/> implementation, typically a subclass
    /// of <see cref="NotificationProviderBase"/>.
    /// </typeparam>
    /// <param name="builder">The mediator service builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IMediatorServiceBuilder UseCustomNotificationProvider<TProvider>(
        this IMediatorServiceBuilder builder)
        where TProvider : class, INotificationProvider =>
        builder.UseNotificationProvider<TProvider>();
}
