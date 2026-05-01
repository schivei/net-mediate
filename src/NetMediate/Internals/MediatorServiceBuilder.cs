using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMediate.Internals.Workers;

namespace NetMediate.Internals;

internal sealed class MediatorServiceBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNotifier>
    : IMediatorServiceBuilder where TNotifier : class, INotifiable
{
    internal static readonly IReadOnlyCollection<Type> s_validInterface =
    [
        typeof(IValidationHandler<>),
        typeof(INotificationHandler<>),
        typeof(IRequestHandler<,>),
        typeof(ICommandHandler<>),
        typeof(IStreamHandler<,>),
        typeof(INotificationBehavior<>),
        typeof(IRequestBehavior<,>),
        typeof(ICommandBehavior<>),
        typeof(IStreamBehavior<,>)
    ];

    private readonly Configuration _configuration;

    public IServiceCollection Services { get; }

    internal MediatorServiceBuilder(IServiceCollection services)
    {
        _configuration = new Configuration(Channel.CreateUnbounded<IPack>());

        Services = services;

        Services.TryAddSingleton<Mediator>();
        Services.TryAddSingleton<IMediator>(sp => sp.GetRequiredService<Mediator>());
        Services.TryAddSingleton(_ => _configuration);

        if (Services.Any(s => s.ServiceType == typeof(INotifiable)))
        {
            Services.Replace(ServiceDescriptor.Singleton<INotifiable, TNotifier>());
        }
        else
        {
            Services.AddSingleton<INotifiable, TNotifier>();
        }

        if (typeof(TNotifier) == typeof(Notifier))
        {
            Services.TryAddSingleton<NotificationWorker>();
            Services.AddHostedService(sp => sp.GetRequiredService<NotificationWorker>());
        }
    }

    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection to discover handler types and may not be compatible with trimming or NativeAOT. " +
        "Use NetMediate.SourceGeneration for a trim-safe, AOT-compatible registration path."
    )]
    internal IMediatorServiceBuilder MapAssemblies(params Assembly[] assemblies)
    {
        if (assemblies is null or { Length: 0 })
        {
            assemblies = [.. AppDomain
                    .CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))];
        }

        var types = ExtractTypes(assemblies);

        foreach (var (handlerType, iface) in types)
        {
            // When the handler is an open-generic type definition the interface returned by
            // GetInterfaces() carries the type parameters of the containing class, NOT the
            // generic type definition itself.  .NET DI requires the generic type definition
            // when registering open-generic services, so normalise the service type here.
            var serviceType = handlerType.IsGenericTypeDefinition && iface.IsGenericType
                ? iface.GetGenericTypeDefinition()
                : iface;

            Services.TryAddEnumerable(ServiceDescriptor.Singleton(serviceType, handlerType));
        }

        return this;
    }

    public IMediatorServiceBuilder IgnoreUnhandledMessages(
        bool ignore = true
    )
    {
        _configuration.IgnoreUnhandledMessages = ignore;

        return this;
    }

    [RequiresUnreferencedCode(
        "Assembly scanning uses reflection to discover handler types and may not be compatible with trimming or NativeAOT."
    )]
    private static IEnumerable<(Type handlerType, Type iface)> ExtractTypes(
        Assembly[] assemblies
    ) =>
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type =>
                type.FindInterfaces((iface, _) => iface.IsGenericType && s_validInterface.Contains(iface.GetGenericTypeDefinition()), null)
                    .Select(iface => (handlerType: type, iface))
            ).Distinct();
}
