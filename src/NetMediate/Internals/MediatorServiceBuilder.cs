using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.Internals;

internal sealed class MediatorServiceBuilder<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TNotifier>
    : IMediatorServiceBuilder where TNotifier : class, INotifiable
{
    /// <summary>Handler interfaces scanned when using the reflection-based assembly overload.</summary>
    private static readonly IReadOnlyList<Type> s_handlerInterfaces =
    [
        typeof(IValidationHandler<>),
        typeof(INotificationHandler<>),
        typeof(IRequestHandler<,>),
        typeof(ICommandHandler<>),
        typeof(IStreamHandler<,>),
    ];

    private readonly IServiceCollection _services;

    internal MediatorServiceBuilder(IServiceCollection services)
    {
        _services = services;

        _services.TryAddSingleton<IMediator, Mediator>();
        
        _services.AddTransient(typeof(PipelineExecutor<,,>));

        if (_services.Any(s => s.ServiceType == typeof(INotifiable)))
        {
            _services.Replace(ServiceDescriptor.Singleton<INotifiable, TNotifier>());
        }
        else
        {
            _services.AddSingleton<INotifiable, TNotifier>();
        }
    }

    [RequiresUnreferencedCode("Assembly scanning uses reflection to discover handler types.")]
    internal MediatorServiceBuilder<TNotifier> MapAssemblies(params Assembly[] assemblies)
    {
        if (assemblies is null or { Length: 0 })
        {
            assemblies = [.. AppDomain
                .CurrentDomain
                .GetAssemblies()
                .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))];
        }

        foreach (var (handlerType, iface) in ExtractHandlers(assemblies))
        {
            var serviceType = handlerType.IsGenericTypeDefinition && iface.IsGenericType
                ? iface.GetGenericTypeDefinition()
                : iface;

            _services.TryAddEnumerable(ServiceDescriptor.Singleton(serviceType, handlerType));
        }

        return this;
    }

    public IMediatorServiceBuilder RegisterHandler<
        TInterface,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage,
        TResult>()
        where TInterface : class, IHandler<TMessage, TResult>
        where THandler : class, TInterface
        where TMessage : notnull
        where TResult : notnull
    {
        _services.AddSingleton<TInterface, THandler>();
        return this;
    }

    public IMediatorServiceBuilder RegisterBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior,
        TMessage,
        TResult>()
        where TBehavior : class, IPipelineBehavior<TMessage, TResult>
        where TMessage : notnull
        where TResult : notnull
    {
        _services.AddTransient<IPipelineBehavior<TMessage, TResult>, TBehavior>();
        return this;
    }

    [RequiresUnreferencedCode("Assembly scanning uses reflection to discover handler types.")]
    private static IEnumerable<(Type handlerType, Type iface)> ExtractHandlers(Assembly[] assemblies) =>
        assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .SelectMany(t =>
                t.FindInterfaces(
                        (iface, _) => iface.IsGenericType && s_handlerInterfaces.Contains(iface.GetGenericTypeDefinition()),
                        null
                    )
                    .Select(iface => (handlerType: t, iface))
            );
}
