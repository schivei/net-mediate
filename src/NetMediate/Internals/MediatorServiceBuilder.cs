using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMediate.Internals.Workers;

namespace NetMediate.Internals;

internal sealed class MediatorServiceBuilder<TNotifier> : IMediatorServiceBuilder where TNotifier : class, INotifiable
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

        Services.TryAddSingleton<ITerminator>(new Terminator(() => Environment.Exit(1)));
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

    internal IMediatorServiceBuilder MapAssemblies(params Assembly[] assemblies)
    {
        if (HasAlreadyRegisterd())
        {
            return this;
        }

        if (assemblies is null or { Length: 0 })
        {
            assemblies = [.. AppDomain
                    .CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))];
        }

        var types = ExtractTypes(assemblies);

        foreach (var (handlerType, iface) in types)
        {
            Services.AddSingleton(iface, handlerType);
        }

        return this;
    }

    private bool HasAlreadyRegisterd() =>
        s_validInterface.Any(iface => Services.Any(s =>
            s.ServiceType.IsGenericType
            && s.ServiceType.GetGenericTypeDefinition() == iface));

    public IMediatorServiceBuilder IgnoreUnhandledMessages(
        bool ignore = true
    )
    {
        _configuration.IgnoreUnhandledMessages = ignore;

        return this;
    }

    private static IEnumerable<(Type handlerType, Type iface)> ExtractTypes(
        Assembly[] assemblies
    ) =>
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .SelectMany(type =>
                type.FindInterfaces((type, criteria) => type.IsGenericType && s_validInterface.Contains(type.GetGenericTypeDefinition()), null)
                    .Select(iface => (handlerType: type, iface))
            ).Distinct();
}
