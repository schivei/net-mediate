using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using NetMediate.Quartz.DependencyInjection;

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
    /// This method configures NetMediate to use Quartz as the notification transport for
    /// <see cref="IMediator.Notify{TMessage}(TMessage, CancellationToken)"/> overloads. Notifications are
    /// serialized and stored in the Quartz job store, enabling crash recovery and cluster-distributed execution.
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
        services.AddOptions<QuartzNotificationOptions>();
        if (configureOptions is not null)
            services.Configure(configureOptions);

        // Capture any IScheduler descriptors already registered by the caller so we can restore them
        // after GenDI runs. GenDI 26.5.13+ scans transitive assemblies and auto-registers
        // Quartz.Impl.RemoteScheduler as IScheduler via a factory that resolves 'string' from DI,
        // which always fails at runtime. We remove those auto-generated registrations and keep only
        // the caller-provided ones.
        var preExisting = new HashSet<ServiceDescriptor>(
            services.Where(d => d.ServiceType == typeof(IScheduler))
        );

        services.AddGenDIServices();

        // Remove any IScheduler registrations that were added by GenDI (not present before the call).
        var genDISchedulerDescriptors = services
            .Where(d => d.ServiceType == typeof(IScheduler) && !preExisting.Contains(d))
            .ToList();
        foreach (var d in genDISchedulerDescriptors)
            services.Remove(d);

        return services;
    }
}
