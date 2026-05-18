using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;
using NetMediate.Quartz.DependencyInjection;

namespace NetMediate.Quartz;

/// <summary>
/// Dependency injection extensions for the NetMediate Quartz integration.
/// </summary>
[ExcludeFromCodeCoverage]
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
    /// <returns>The <see cref="IServiceCollection"/> for chaining.</returns>
    [RequiresDynamicCode(
        "QuartzNotificationJob uses MakeGenericMethod for per-type notification dispatch and is not compatible with NativeAOT."
    )]
    [RequiresUnreferencedCode(
        "QuartzNotificationJob uses reflection to resolve message types by name and dispatch notifications."
    )]
    public static IServiceCollection AddNetMediateQuartz(
        this IServiceCollection services
    )
    {
        services.AddOptions<QuartzNotificationOptions>()
            .BindConfiguration(nameof(QuartzNotificationOptions));

        // Capture any IScheduler descriptors already registered by the caller so we can restore them
        // after GenDI runs. GenDI 26.5.13+ scans transitive assemblies and auto-registers
        // Quartz.Impl.RemoteScheduler as IScheduler via a factory that resolves 'string' from DI,
        // which always fails at runtime. We remove those auto-generated registrations and keep only
        // the caller-provided ones.
        var preExisting = new HashSet<ServiceDescriptor>(
            services.Where(d => d.ServiceType == typeof(IScheduler))
        );

        services.AddGenDIServices();

        // Apply QuartzMediator as the IMediator decorator.
        // Apply QuartzNotifier as the INotifiable decorator.
        // GenDI cross-assembly decorator support does not generate the wrapping registration when the
        // decorated service type is defined in a referenced assembly, so we apply both decorators explicitly.
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];

            if (descriptor.ServiceType == typeof(IMediator))
            {
                services[i] = WrapWithDecorator<IMediator>(
                    descriptor,
                    (serviceProvider, inner) => new QuartzMediator
                    {
                        Inner = inner,
                        Scheduler = serviceProvider.GetRequiredService<IScheduler>(),
                        Serializer = serviceProvider.GetRequiredService<INotificationSerializer>(),
                        Options = serviceProvider.GetRequiredService<IOptions<QuartzNotificationOptions>>(),
                        Logger = serviceProvider.GetRequiredService<ILogger<QuartzMediator>>(),
                    }
                );
            }
            else if (descriptor.ServiceType == typeof(INotifiable))
            {
                services[i] = WrapWithDecorator<INotifiable>(
                    descriptor,
                    (serviceProvider, inner) => new QuartzNotifier
                    {
                        Inner = inner,
                        Logger = serviceProvider.GetRequiredService<ILogger<QuartzNotifier>>(),
                    }
                );
            }
        }

        // Remove any IScheduler registrations that were added by GenDI (not present before the call).
        var genDISchedulerDescriptors = services
            .Where(d => d.ServiceType == typeof(IScheduler) && !preExisting.Contains(d))
            .ToList();
        foreach (var d in genDISchedulerDescriptors)
            services.Remove(d);

        return services;
    }

    private static ServiceDescriptor WrapWithDecorator<TService>(
        ServiceDescriptor descriptor,
        Func<IServiceProvider, TService, TService> decorate
    )
        where TService : class
    {
        if (descriptor.IsKeyedService)
        {
            return ServiceDescriptor.DescribeKeyed(
                typeof(TService),
                descriptor.ServiceKey,
                (serviceProvider, key) => decorate(
                    serviceProvider,
                    CreateServiceInstance<TService>(descriptor, serviceProvider, key)
                ),
                descriptor.Lifetime
            );
        }

        return ServiceDescriptor.Describe(
            typeof(TService),
            serviceProvider => decorate(
                serviceProvider,
                CreateServiceInstance<TService>(descriptor, serviceProvider, null)
            ),
            descriptor.Lifetime
        );
    }

    private static TService CreateServiceInstance<TService>(
        ServiceDescriptor descriptor,
        IServiceProvider serviceProvider,
        object? serviceKey
    )
        where TService : class
    {
        if (descriptor.IsKeyedService)
        {
            if (descriptor.KeyedImplementationFactory is not null)
                return (TService)descriptor.KeyedImplementationFactory(serviceProvider, serviceKey);

            if (descriptor.KeyedImplementationInstance is not null)
                return (TService)descriptor.KeyedImplementationInstance;

            if (descriptor.KeyedImplementationType is not null)
                return (TService)ActivatorUtilities.CreateInstance(serviceProvider, descriptor.KeyedImplementationType);
        }
        else
        {
            if (descriptor.ImplementationFactory is not null)
                return (TService)descriptor.ImplementationFactory(serviceProvider);

            if (descriptor.ImplementationInstance is not null)
                return (TService)descriptor.ImplementationInstance;

            if (descriptor.ImplementationType is not null)
                return (TService)ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType);
        }

        throw new InvalidOperationException(
            $"Service descriptor for '{typeof(TService).FullName}' does not contain an implementation."
        );
    }
}
