using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;
using Quartz;

namespace NetMediate.Quartz;

/// <summary>
/// Dependency injection extensions for the NetMediate Quartz integration.
/// </summary>
public static class NetMediateQuartzDI
{
    /// <summary>
    /// Registers <see cref="QuartzNotifier"/> as the <see cref="INotifiable"/> implementation so that every
    /// NetMediate notification is persisted as a Quartz job before being dispatched to handlers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method configures NetMediate to use Quartz as the notification transport. Notifications are
    /// serialized and stored in the Quartz job store instead of the default in-memory channel, enabling
    /// crash recovery and cluster-distributed execution.
    /// </para>
    /// <para>
    /// Quartz must be configured and its <see cref="IScheduler"/> must be registered in the service
    /// collection before calling this method. Use <c>services.AddQuartz()</c> and
    /// <c>services.AddQuartzHostedService()</c> (from <c>Quartz.Extensions.Hosting</c>) to complete the
    /// Quartz setup. For persistent job stores, configure an <c>AdoJobStore</c> in your Quartz options.
    /// </para>
    /// <para>
    /// <see cref="QuartzNotificationJob"/> is registered as a Quartz job and resolved through the
    /// Microsoft DI container via <c>MicrosoftDependencyInjectionJobFactory</c>.
    /// </para>
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add NetMediate Quartz services to.</param>
    /// <param name="configureOptions">Optional callback to configure <see cref="QuartzNotificationOptions"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    [RequiresDynamicCode(
        "QuartzNotificationJob uses MakeGenericMethod for per-type notification dispatch and is not compatible with NativeAOT."
    )]
    [RequiresUnreferencedCode(
        "QuartzNotificationJob uses reflection to resolve message types by name and dispatch notifications."
    )]
    public static IServiceCollection AddNetMediateQuartz(
        this IServiceCollection services,
        Action<QuartzNotificationOptions>? configureOptions = null
    )
    {
        var opts = new QuartzNotificationOptions();
        configureOptions?.Invoke(opts);
        services.AddSingleton(opts);

        services.AddSingleton<INotificationSerializer, JsonNotificationSerializer>();

        services.AddTransient<QuartzNotificationJob>();

        services.UseNetMediate<QuartzNotifier>(_ => { });

        return services;
    }
}
