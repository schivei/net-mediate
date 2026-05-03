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

    internal MediatorServiceBuilder(IServiceCollection services)
    {
        _services = services;

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
        TResult>()
        where TInterface : class, IHandler<TMessage, TResult>
        where THandler : class, TInterface
        where TMessage : notnull
        where TResult : notnull
    {
        _services.AddSingleton<TInterface, THandler>();
        return this;
    }

    // ── Specialized registration (AOT-safe, used by source generator) ──
    // Each method name groups its type-based and instance-based overloads together.

    public IMediatorServiceBuilder RegisterCommandHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage>()
        where THandler : class, ICommandHandler<TMessage>
        where TMessage : notnull
    {
        _services.AddSingleton<ICommandHandler<TMessage>, THandler>();
        _services.TryAddSingleton<PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>>();
        return this;
    }

    public IMediatorServiceBuilder RegisterCommandHandler<TMessage>(ICommandHandler<TMessage> handler)
        where TMessage : notnull
    {
        _services.AddSingleton<ICommandHandler<TMessage>>(handler);
        _services.TryAddSingleton<PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>>();
        return this;
    }

    public IMediatorServiceBuilder RegisterNotificationHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage>()
        where THandler : class, INotificationHandler<TMessage>
        where TMessage : notnull
    {
        _services.AddSingleton<INotificationHandler<TMessage>, THandler>();
        _services.TryAddSingleton<NotificationPipelineExecutor<TMessage>>();
        return this;
    }

    public IMediatorServiceBuilder RegisterNotificationHandler<TMessage>(INotificationHandler<TMessage> handler)
        where TMessage : notnull
    {
        _services.AddSingleton<INotificationHandler<TMessage>>(handler);
        _services.TryAddSingleton<NotificationPipelineExecutor<TMessage>>();
        return this;
    }

    public IMediatorServiceBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage,
        TResponse>()
        where THandler : class, IRequestHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        _services.AddSingleton<IRequestHandler<TMessage, TResponse>, THandler>();
        _services.TryAddSingleton<RequestPipelineExecutor<TMessage, TResponse>>();
        return this;
    }

    public IMediatorServiceBuilder RegisterRequestHandler<TMessage, TResponse>(IRequestHandler<TMessage, TResponse> handler)
        where TMessage : notnull
    {
        _services.AddSingleton<IRequestHandler<TMessage, TResponse>>(handler);
        _services.TryAddSingleton<RequestPipelineExecutor<TMessage, TResponse>>();
        return this;
    }

    public IMediatorServiceBuilder RegisterStreamHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        THandler,
        TMessage,
        TResponse>()
        where THandler : class, IStreamHandler<TMessage, TResponse>
        where TMessage : notnull
    {
        _services.AddSingleton<IStreamHandler<TMessage, TResponse>, THandler>();
        _services.TryAddSingleton<StreamPipelineExecutor<TMessage, TResponse>>();
        return this;
    }

    public IMediatorServiceBuilder RegisterStreamHandler<TMessage, TResponse>(IStreamHandler<TMessage, TResponse> handler)
        where TMessage : notnull
    {
        _services.AddSingleton<IStreamHandler<TMessage, TResponse>>(handler);
        _services.TryAddSingleton<StreamPipelineExecutor<TMessage, TResponse>>();
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
}