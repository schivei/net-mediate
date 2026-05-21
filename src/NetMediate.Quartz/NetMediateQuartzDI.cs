using Microsoft.Extensions.DependencyInjection;
using Quartz;

[assembly: GenDICoveration(false)]

namespace NetMediate.Quartz;

/// <summary>
/// Dependency injection extensions for the NetMediate Quartz integration.
/// </summary>
public static class NetMediateQuartzDI
{
    /// <summary>
    /// Registers NetMediate Quartz services so <see cref="QuartzMediator"/> decorates
    /// <see cref="IMediator"/> notification publishing with Quartz job persistence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only call this method before the service provider is built and before any <see cref="IMediator"/> or notification handler services are resolved.
    /// </para>
    /// <para>
    /// This method configures NetMediate to use Quartz as the notification transport for
    /// <see cref="IMediator.Notify{TMessage}(TMessage)"/> overloads. Notifications are
    /// serialized and stored in the Quartz job store, enabling crash recovery and cluster-distributed execution.
    /// </para>
    /// <para>
    /// Quartz must be configured and its <see cref="IScheduler"/> must be registered in the service
    /// collection before calling this method. Use <c>services.AddQuartz()</c> and
    /// <c>services.AddQuartzHostedService()</c> (from <c>Quartz.Extensions.Hosting</c>) to complete the
    /// Quartz setup. For persistent job stores, configure an <c>AdoJobStore</c> in your Quartz options.
    /// </para>
    /// <para>
    /// <see cref="QuartzNotificationJob{TMessage}"/> is registered as a Quartz job and resolved through the
    /// Microsoft DI container via <c>MicrosoftDependencyInjectionJobFactory</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add NetMediate Quartz services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddNetMediateQuartz(
        this IServiceCollection services
    )
    {
        services.AddNetMediate();

        return services;
    }
}
