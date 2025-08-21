using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NetMediate.Internals.Workers;

namespace NetMediate.Internals;

internal sealed class MediatorServiceBuilder : IMediatorServiceBuilder
{
    private readonly Configuration _configuration;
    private readonly HashSet<int> _assemblyHashCodes = [];

    internal MediatorServiceBuilder(IServiceCollection services)
    {
        _configuration = new Configuration(Channel.CreateUnbounded<object>());

        Services = services;

        Services.TryAddSingleton<IMediator, Mediator>();
        Services.TryAddSingleton<INotifiable>(sp => sp.GetRequiredService<IMediator>() as Mediator);
        Services.TryAddSingleton(_ => _configuration);

        if (!Services.Any(s => s.ServiceType == typeof(NotificationWorker)))
        {
            Services.TryAddSingleton<NotificationWorker>();
            Services.AddHostedService(sp => sp.GetRequiredService<NotificationWorker>());
        }
    }

    public IServiceCollection Services { get; }

    internal IMediatorServiceBuilder MapAssemblies(params Assembly[] assemblies)
    {
        var assemblyHashCodes = assemblies
            .Where(a => !_assemblyHashCodes.Contains(a.GetHashCode()))
            .ToArray();
        if (assemblyHashCodes.Length == 0)
            return this;

        foreach (var assembly in assemblyHashCodes)
            _assemblyHashCodes.Add(assembly.GetHashCode());

        var types = ExtractTypes(assemblies);

        MapValidationHandlers(types);
        MapNotificationHandlers(types);
        MapCommandHandlers(types);
        MapRequestHandlers(types);
        MapStreamHandlers(types);

        return this;
    }

    public IMediatorServiceBuilder IgnoreUnhandledMessages(
        bool ignore = true,
        bool log = true,
        LogLevel logLevel = LogLevel.Error
    )
    {
        _configuration.IgnoreUnhandledMessages = ignore;
        _configuration.LogUnhandledMessages = log;
        _configuration.UnhandledMessagesLogLevel = logLevel;

        return this;
    }

    private MediatorServiceBuilder Filter<TMessage, THandler, TBase>(Func<TMessage, bool> filter)
    {
        RegisterType(typeof(TBase), typeof(THandler));

        _configuration.InstantiateHandlerByMessageFilter<TMessage>(message =>
        {
            if (filter(message))
                return typeof(THandler);
            return null;
        });

        return this;
    }

    public IMediatorServiceBuilder FilterNotification<TMessage, THandler>(
        Func<TMessage, bool> filter
    )
        where THandler : class, INotificationHandler<TMessage> =>
        Filter<TMessage, THandler, INotificationHandler<TMessage>>(filter);

    public IMediatorServiceBuilder FilterCommand<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, ICommandHandler<TMessage> =>
        Filter<TMessage, THandler, ICommandHandler<TMessage>>(filter);

    public IMediatorServiceBuilder FilterRequest<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, IRequestHandler<TMessage, object> =>
        Filter<TMessage, THandler, IRequestHandler<TMessage, object>>(filter);

    public IMediatorServiceBuilder FilterStream<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, IStreamHandler<TMessage, object> =>
        Filter<TMessage, THandler, IStreamHandler<TMessage, object>>(filter);

    public IMediatorServiceBuilder InstantiateHandlerByMessageFilter<TMessage>(
        Func<TMessage, Type?> filter
    )
    {
        _configuration.InstantiateHandlerByMessageFilter(filter);

        return this;
    }

    private static readonly Type[] s_validInterface =
    [
        typeof(IValidationHandler<>),
        typeof(INotificationHandler<>),
        typeof(IRequestHandler<,>),
        typeof(ICommandHandler<>),
        typeof(IStreamHandler<,>),
    ];

    private void MapValidationHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(IValidationHandler<>));

    private void MapNotificationHandlers(
        IEnumerable<(Type handlerType, Type[] interfaces)> types
    ) => Map(types, typeof(INotificationHandler<>));

    private void MapRequestHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(IRequestHandler<,>));

    private void MapCommandHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(ICommandHandler<>));

    private void MapStreamHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(IStreamHandler<,>));

    public IMediatorServiceBuilder Register(Type messageType, Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentNullException.ThrowIfNull(handlerType);

        var interfaces = GetInterfaces(handlerType, messageType);
        Map([(handlerType, interfaces)]);

        return this;
    }

    private static Type[] GetInterfaces(Type handlerType, Type messageType)
    {
        if (!handlerType.IsClass || handlerType.IsAbstract)
        {
            throw new ArgumentException(
                $"Handler type '{handlerType.FullName}' must be a non-abstract class.",
                nameof(handlerType)
            );
        }

        var interfaces = handlerType
            .GetInterfaces()
            .Where(i =>
                i.IsGenericType
                && s_validInterface.Contains(i.GetGenericTypeDefinition())
                && i.GenericTypeArguments.Length >= 1
                && i.GenericTypeArguments[0] == messageType
            )
            .ToArray();

        if (interfaces.Length == 0)
        {
            throw new ArgumentException(
                $"Handler type '{handlerType.FullName}' does not implement any valid handler interfaces.",
                nameof(handlerType)
            );
        }

        return interfaces;
    }

    private void Map(
        IEnumerable<(Type handlerType, Type[] interfaces)> types,
        Type? handlerInterface = null
    )
    {
        handlerInterface ??= types
            .Select(
                (type, _) =>
                    type.interfaces.First(ifce =>
                        s_validInterface.Contains(ifce.GetGenericTypeDefinition())
                    )
            )
            .First()?
            .GetGenericTypeDefinition();

        var handlerTypes = types
            .Where(type =>
                type.interfaces.Any(i =>
                    i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface
                )
            )
            .Select(type =>
                (
                    type.handlerType,
                    interfaceType: type.interfaces.First(x =>
                        x.IsGenericType && x.GetGenericTypeDefinition() == handlerInterface
                    )
                )
            )
            .ToList();

        foreach (var (handlerType, interfaceType) in handlerTypes)
            RegisterType(interfaceType, handlerType);
    }

    private void RegisterType(Type interfaceType, Type handlerType)
    {
        var unique =
            interfaceType.GetGenericTypeDefinition() != typeof(INotificationHandler<>)
            && interfaceType.GetGenericTypeDefinition() != typeof(IValidationHandler<>);

        if (unique)
            UniqueRegisterType(interfaceType, handlerType);
        else
            MultiRegisterType(interfaceType, handlerType);
    }

    private void UniqueRegisterType(Type interfaceType, Type handlerType)
    {
        var keyed = handlerType.GetKey();

        if (keyed is not null)
            Services.TryAddKeyedScoped(interfaceType, keyed, handlerType);
        else
            Services.TryAddScoped(interfaceType, handlerType);
    }

    private void MultiRegisterType(Type interfaceType, Type handlerType)
    {
        var keyed = handlerType.GetKey();
        if (keyed is not null)
            Services.AddKeyedTransient(interfaceType, keyed, handlerType);
        else
            Services.AddTransient(interfaceType, handlerType);
    }

    [ExcludeFromCodeCoverage]
    private static IEnumerable<(Type handlerType, Type[] interfaces)> ExtractTypes(
        Assembly[] assemblies
    ) =>
        assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type =>
                type.IsClass
                && !type.IsAbstract
                && type.GetInterfaces()
                    .Any(i =>
                        i.IsGenericType && s_validInterface.Contains(i.GetGenericTypeDefinition())
                    )
            )
            .Select(type =>
                (
                    handlerType: type,
                    interfaces: type.GetInterfaces()
                        .Where(i =>
                            i.IsGenericType
                            && s_validInterface.Contains(i.GetGenericTypeDefinition())
                        )
                        .ToArray()
                )
            );
}
