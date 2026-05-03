using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.Adapters;

/// <summary>
/// Dependency injection extensions for NetMediate notification adapters.
/// </summary>
public static class NetMediateAdaptersDI
{
    /// <summary>
    /// Registers the <see cref="NotificationAdapterBehavior{TMessage}"/> pipeline behavior so that all
    /// <see cref="INotificationAdapter{TMessage}"/> implementations are invoked after core notification
    /// handlers complete.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configureOptions">Optional callback to configure <see cref="NotificationAdapterOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddNetMediateAdapters(
        this IServiceCollection services,
        Action<NotificationAdapterOptions>? configureOptions = null)
    {
        var opts = new NotificationAdapterOptions();
        configureOptions?.Invoke(opts);
        services.AddSingleton(opts);

        services.AddSingleton(typeof(IPipelineBehavior<>), typeof(NotificationAdapterBehavior<>));

        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="INotificationAdapter{TMessage}"/> implementation for a specific
    /// notification message type.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type the adapter handles.</typeparam>
    /// <typeparam name="TAdapter">The adapter implementation type.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddNotificationAdapter<TMessage, TAdapter>(
        this IServiceCollection services)
        where TMessage : notnull
        where TAdapter : class, INotificationAdapter<TMessage>
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<INotificationAdapter<TMessage>, TAdapter>());
        return services;
    }

    /// <summary>
    /// Registers a concrete <see cref="INotificationAdapter{TMessage}"/> instance for a specific
    /// notification message type.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type the adapter handles.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="adapter">The adapter instance to register.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddNotificationAdapter<TMessage>(
        this IServiceCollection services,
        INotificationAdapter<TMessage> adapter)
        where TMessage : notnull
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton(adapter));
        return services;
    }
}
