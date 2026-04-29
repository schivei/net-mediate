using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal sealed class MediatorServiceBuilder : IMediatorServiceBuilder
{
    private readonly Configuration _configuration;
    private readonly HashSet<int> _assemblyHashCodes = [];

    internal MediatorServiceBuilder(IServiceCollection services)
    {
        _configuration = new Configuration(Channel.CreateUnbounded<INotificationPacket>());

        Services = services;

        Services.TryAddSingleton<IMediator, Mediator>();
        Services.TryAddSingleton<INotifiable>(sp => sp.GetRequiredService<IMediator>() as Mediator);
        Services.TryAddSingleton(_ => _configuration);
        Services.TryAddSingleton<INotificationProvider, BuiltInNotificationProvider>();
        Services.TryAddSingleton<INotificationDispatcher>(sp =>
            (INotificationDispatcher)sp.GetRequiredService<IMediator>());
        Services.AddHostedService<Workers.NotificationWorker>();
    }

    public IServiceCollection Services { get; }

#if NET5_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Scans assembly types using reflection. Not compatible with trimming or Native AOT."
    )]
#endif
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

    /// <inheritdoc/>
    public IMediatorServiceBuilder DisableTelemetry()
    {
        _configuration.EnableTelemetry = false;
        return this;
    }

    /// <inheritdoc/>
    public IMediatorServiceBuilder DisableValidation()
    {
        _configuration.EnableValidation = false;
        return this;
    }

    /// <inheritdoc/>
    public IMediatorServiceBuilder UseNotificationProvider<TProvider>()
        where TProvider : class, INotificationProvider
    {
        _configuration.EnableBuiltInWorker = false;
        Services.Replace(ServiceDescriptor.Singleton<INotificationProvider, TProvider>());
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

    public IMediatorServiceBuilder FilterRequest<TMessage, TResponse, THandler>(
        Func<TMessage, bool> filter
    )
        where THandler : class, IRequestHandler<TMessage, TResponse> =>
        Filter<TMessage, THandler, IRequestHandler<TMessage, TResponse>>(filter);

    public IMediatorServiceBuilder FilterStream<TMessage, TResponse, THandler>(
        Func<TMessage, bool> filter
    )
        where THandler : class, IStreamHandler<TMessage, TResponse> =>
        Filter<TMessage, THandler, IStreamHandler<TMessage, TResponse>>(filter);

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

    private void MapValidationHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types)
    {
        var validationTypes = types
            .Where(t => t.interfaces.Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IValidationHandler<>)))
            .ToList();

        Map(validationTypes, typeof(IValidationHandler<>));

        // Track which message types have registered validators at startup so dispatch can
        // skip the validation DI resolution entirely when no validators exist for a type.
        foreach (var (_, interfaces) in validationTypes)
        {
            foreach (var iface in interfaces)
            {
                if (iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IValidationHandler<>))
                    _configuration.MarkAsValidatable(iface.GenericTypeArguments[0]);
            }
        }
    }

    private void MapNotificationHandlers(
        IEnumerable<(Type handlerType, Type[] interfaces)> types
    ) => Map(types, typeof(INotificationHandler<>));

    private void MapRequestHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(IRequestHandler<,>));

    private void MapCommandHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(ICommandHandler<>));

    private void MapStreamHandlers(IEnumerable<(Type handlerType, Type[] interfaces)> types) =>
        Map(types, typeof(IStreamHandler<,>));

    public IMediatorServiceBuilder RegisterNotificationHandler<TMessage, THandler>()
        where THandler : class, INotificationHandler<TMessage> =>
        Register(typeof(TMessage), typeof(THandler));

    public IMediatorServiceBuilder RegisterCommandHandler<TMessage, THandler>()
        where THandler : class, ICommandHandler<TMessage> =>
        Register(typeof(TMessage), typeof(THandler));

    public IMediatorServiceBuilder RegisterRequestHandler<TMessage, TResponse, THandler>()
        where THandler : class, IRequestHandler<TMessage, TResponse> =>
        Register(typeof(TMessage), typeof(THandler));

    public IMediatorServiceBuilder RegisterStreamHandler<TMessage, TResponse, THandler>()
        where THandler : class, IStreamHandler<TMessage, TResponse> =>
        Register(typeof(TMessage), typeof(THandler));

    public IMediatorServiceBuilder RegisterValidationHandler<TMessage, THandler>()
        where THandler : class, IValidationHandler<TMessage>
    {
        // Mark the message type as validatable so the dispatch fast-path knows to run validation.
        _configuration.MarkAsValidatable(typeof(TMessage));
        return Register(typeof(TMessage), typeof(THandler));
    }

    public IMediatorServiceBuilder RegisterValidationHandler(Type messageType, Type handlerType)
    {
        Guard.ThrowIfNull(messageType);
        Guard.ThrowIfNull(handlerType);

        // Mark the message type as validatable so the dispatch fast-path knows to run validation.
        _configuration.MarkAsValidatable(messageType);
        return Register(messageType, handlerType);
    }

    public IMediatorServiceBuilder Register(Type messageType, Type handlerType)
    {
        Guard.ThrowIfNull(messageType);
        Guard.ThrowIfNull(handlerType);

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
            .Select(type =>
                type.interfaces.FirstOrDefault(ifce =>
                    s_validInterface.Contains(ifce.GetGenericTypeDefinition())
                )
            )
            .FirstOrDefault(t => t is not null)
            ?.GetGenericTypeDefinition();

        if (handlerInterface is null)
        {
            throw new ArgumentException(
                "No valid handler interface found in the provided types.",
                nameof(handlerInterface)
            );
        }

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
            Services.TryAddKeyedSingleton(interfaceType, keyed, handlerType);
        else
            Services.TryAddSingleton(interfaceType, handlerType);
    }

    private void MultiRegisterType(Type interfaceType, Type handlerType)
    {
        var keyed = handlerType.GetKey();
        if (keyed is not null)
            Services.AddKeyedSingleton(interfaceType, keyed, handlerType);
        else
            Services.AddSingleton(interfaceType, handlerType);
    }

    [ExcludeFromCodeCoverage]
#if NET5_0_OR_GREATER
    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode(
        "Scans assembly types using reflection. Not compatible with trimming or Native AOT."
    )]
#endif
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
