using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.Internals.Workers;
using System.Reflection;
using System.Threading.Channels;

namespace NetMediate.Internals;

public sealed class MediatorConfiguration
{
    private readonly Configuration _configuration;

    internal MediatorConfiguration(IServiceCollection services)
    {
        _configuration = new Configuration(Channel.CreateUnboundedPrioritized<object>());

        Services = services;

        Services.AddSingleton<IMediator, Mediator>();
        Services.AddSingleton(_ => _configuration);
        Services.AddHostedService<NotificationWorker>();
    }

    public IServiceCollection Services { get; }

    internal MediatorConfiguration MapAssembly<T>() =>
        MapAssemblies(typeof(T).Assembly);

    internal MediatorConfiguration MapAssemblies(params Assembly[] assemblies)
    {
        if (assemblies.Length == 0)
            assemblies = [Assembly.GetExecutingAssembly()];

        var types = ExtractTypes(assemblies);

        MapValidationHandlers(types);
        MapNotificationHandlers(types);
        MapCommandHandlers(types);
        MapRequestHandlers(types);
        MapStreamHandlers(types);

        return this;
    }

    public MediatorConfiguration IgnoreUnhandledMessages(bool ignore = true, bool log = true, LogLevel logLevel = LogLevel.Error)
    {
        _configuration.IgnoreUnhandledMessages = ignore;
        _configuration.LogUnhandledMessages = log;
        _configuration.UnhandledMessagesLogLevel = logLevel;

        return this;
    }

    public MediatorConfiguration FilterNotification<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, INotificationHandler<TMessage>
    {
        Register(typeof(INotificationHandler<TMessage>), typeof(THandler), false);

        _configuration.InstantiateHandlerByMessageFilter<TMessage>(message =>
        {
            if (filter(message))
                return typeof(THandler);
            return null;
        });

        return this;
    }

    public MediatorConfiguration FilterCommand<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, ICommandHandler<TMessage>
    {
        Register(typeof(INotificationHandler<TMessage>), typeof(THandler), false);

        _configuration.InstantiateHandlerByMessageFilter<TMessage>(message =>
        {
            if (filter(message))
                return typeof(THandler);
            return null;
        });

        return this;
    }

    public MediatorConfiguration FilterRequest<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, IRequestHandler<TMessage, object>
    {
        Register(typeof(IRequestHandler<TMessage, object>), typeof(THandler), false);
        _configuration.InstantiateHandlerByMessageFilter<TMessage>(message =>
        {
            if (filter(message))
                return typeof(THandler);
            return null;
        });
        return this;
    }

    public MediatorConfiguration FilterStream<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, IStreamHandler<TMessage, object>
    {
        Register(typeof(IStreamHandler<TMessage, object>), typeof(THandler), false);
        _configuration.InstantiateHandlerByMessageFilter<TMessage>(message =>
        {
            if (filter(message))
                return typeof(THandler);
            return null;
        });
        return this;
    }

    public MediatorConfiguration InstantiateHandlerByMessageFilter<TMessage>(Func<TMessage, Type?> filter)
    {
        _configuration.InstantiateHandlerByMessageFilter(filter);

        return this;
    }

    private void MapValidationHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(IValidationHandler<>), false);

    private void MapNotificationHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(INotificationHandler<>), false);

    private void MapRequestHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(IRequestHandler<,>));

    private void MapCommandHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(ICommandHandler<>));

    private void MapStreamHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(IStreamHandler<,>));

    private void Map(IEnumerable<(Type handlerType, Type[] interfaces)> types, Type handlerInterface, bool unique = true)
    {
        var handlerTypes = types
                    .Where(type => type.interfaces.Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == handlerInterface))
                    .Select(type => (type.handlerType, interfaceType: type.interfaces.First(x => x.IsGenericType && x.GetGenericTypeDefinition() == handlerInterface)))
                    .ToList();

        foreach (var (handlerType, interfaceType) in handlerTypes)
            Register(interfaceType, handlerType, unique);
    }

    private void Register(Type interfaceType, Type handlerType, bool unique = true)
    {
        var keyed = handlerType.GetKey();

        if (Services.Any(s => s.ServiceType == interfaceType && s.ImplementationType == handlerType && s.ServiceKey?.ToString() == keyed))
            return;

        if (keyed is not null)
        {
            if (unique && Services.Any(s => s.ServiceType == interfaceType && s.ServiceKey?.ToString() == keyed))
                throw new InvalidOperationException($"Service {interfaceType.Name} with key '{keyed}' is already registered.");

            Services.AddKeyedScoped(interfaceType, keyed, handlerType);
            return;
        }

        if (unique && Services.Any(s => s.ServiceType == interfaceType))
            throw new InvalidOperationException($"Service {interfaceType.Name} is already registered.");

        Services.AddScoped(interfaceType, handlerType);
    }

    private static IEnumerable<(Type handlerType, Type[] interfaces)> ExtractTypes(Assembly[] assemblies) =>
        assemblies
            .SelectMany(assembly => assembly.ExportedTypes)
            .Where(type => type.IsClass && !type.IsAbstract && type.GetInterfaces().Length != 0)
            .Select(type => (handlerType: type, interfaces: type.GetInterfaces()));
}
