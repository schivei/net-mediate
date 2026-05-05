using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.Internals;

internal sealed class MediatorServiceBuilder<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
    TNotifier>
    : IMediatorServiceBuilder where TNotifier : class, INotifiable
{
    private readonly IServiceCollection _services;

    public IServiceCollection Services => _services;

    internal MediatorServiceBuilder(IServiceCollection services, bool skipCoreRegistration = false)
    {
        _services = services;

        if (skipCoreRegistration)
            return;

        _services.TryAddSingleton<IMediator, Mediator>();

        if (_services.Any(s => s.ServiceType == typeof(INotifiable)))
        {
            _services.Replace(ServiceDescriptor.Singleton<INotifiable, TNotifier>());
        }
        else
        {
            _services.AddSingleton<INotifiable, TNotifier>();
        }
    }

    public IMediatorServiceBuilder RegisterHandler< // NOSONAR S2436
        TInterface,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage,
        TResult>(object? key = null)
        where TInterface : class, IHandler<TMessage, TResult>
        where THandler : class, TInterface
        where TMessage : notnull
        where TResult : notnull
    {
        if (key is not null)
        {
            _services.AddKeyedSingleton<TInterface, THandler>(key);
            return this;
        }

        _services.AddSingleton<TInterface, THandler>();
        return this;
    }

    // ── Specialized registration (AOT-safe, used by source generator) ──
    // Each method name groups its type-based and instance-based overloads together.

    public IMediatorServiceBuilder RegisterCommandHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage>(object? key = null)
        where THandler : class, ICommandHandler<TMessage>
        where TMessage : notnull
    {

        if (key is not null)
        {
            _services.TryAddKeyedSingleton<PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>>(key);
            _services.AddKeyedSingleton<ICommandHandler<TMessage>, THandler>(key);
            return this;
        }

        _services.TryAddSingleton<PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>>();
        _services.AddSingleton<ICommandHandler<TMessage>, THandler>();
        return this;
    }

    public IMediatorServiceBuilder RegisterNotificationHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage>(object? key = null)
        where THandler : class, INotificationHandler<TMessage>
        where TMessage : notnull
    {
        if (key is not null)
        {
            _services.TryAddKeyedSingleton<NotificationPipelineExecutor<TMessage>>(key);
            _services.AddKeyedSingleton<INotificationHandler<TMessage>, THandler>(key);
            return this;
        }
        _services.TryAddSingleton<NotificationPipelineExecutor<TMessage>>();
        _services.AddSingleton<INotificationHandler<TMessage>, THandler>();
        return this;
    }

    public IMediatorServiceBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage,
        TResponse>(object? key = null)
        where THandler : class, IRequestHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        if (key is not null)
        {
            _services.TryAddKeyedSingleton<RequestPipelineExecutor<TMessage, TResponse>>(key);
            _services.AddKeyedSingleton<IRequestHandler<TMessage, TResponse>, THandler>(key);
            return this;
        }

        _services.TryAddSingleton<RequestPipelineExecutor<TMessage, TResponse>>();
        _services.AddSingleton<IRequestHandler<TMessage, TResponse>, THandler>();
        return this;
    }

    public IMediatorServiceBuilder RegisterStreamHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage,
        TResponse>(object? key = null)
        where THandler : class, IStreamHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        if (key is not null)
        {
            _services.TryAddKeyedSingleton<StreamPipelineExecutor<TMessage, TResponse>>(key);
            _services.AddKeyedSingleton<IStreamHandler<TMessage, TResponse>, THandler>(key);
            return this;
        }

        _services.TryAddSingleton<StreamPipelineExecutor<TMessage, TResponse>>();
        _services.AddSingleton<IStreamHandler<TMessage, TResponse>, THandler>();
        return this;
    }

    public IMediatorServiceBuilder RegisterBehavior< // NOSONAR S2436
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

    public IMediatorServiceBuilder RegisterNotificationBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        TBehavior,
        TMessage>()
        where TBehavior : class, IPipelineNotificationBehavior<TMessage>
        where TMessage : notnull
    {
        _services.AddTransient<IPipelineNotificationBehavior<TMessage>, TBehavior>();
        return this;
    }
}