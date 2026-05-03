using System.Diagnostics.CodeAnalysis;

namespace NetMediate;

/// <summary>
/// Defines a builder interface for registering handlers in the mediator service.
/// This interface allows for fluent configuration of message handlers,
/// enabling the registration of handlers for specific message types and result types.
/// Handlers can be registered with or without custom instantiation logic,
/// providing flexibility in how handlers are created and managed within the mediator framework.
/// </summary>
public interface IMediatorServiceBuilder
{
    /// <summary>
    /// Registers a handler for a specific message type and result type.
    /// </summary>
    /// <typeparam name="TInterface">The interface type that the handler implements.</typeparam>
    /// <typeparam name="THandler">The concrete handler type.</typeparam>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <returns>The current instance of <see cref="IMediatorServiceBuilder"/> for chaining.</returns>
    IMediatorServiceBuilder RegisterHandler< // NOSONAR S2436
        TInterface,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage,
        TResult>()
        where TInterface : class, IHandler<TMessage, TResult>
        where THandler : class, TInterface
        where TMessage : notnull
        where TResult : notnull;

    // ── Type-based specialized registration (AOT-safe, used by source generator) ──

    /// <summary>
    /// Registers a command handler and its closed-type pipeline executor.
    /// Prefer this over <see cref="RegisterHandler{TInterface,THandler,TMessage,TResult}"/> for commands.
    /// </summary>
    IMediatorServiceBuilder RegisterCommandHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage>()
        where THandler : class, ICommandHandler<TMessage>
        where TMessage : notnull;

    /// <summary>
    /// Registers a notification handler and its closed-type pipeline executor.
    /// </summary>
    IMediatorServiceBuilder RegisterNotificationHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage>()
        where THandler : class, INotificationHandler<TMessage>
        where TMessage : notnull;

    /// <summary>
    /// Registers a request handler and its closed-type pipeline executor.
    /// </summary>
    IMediatorServiceBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage,
        TResponse>()
        where THandler : class, IRequestHandler<TMessage, TResponse>
        where TMessage : notnull;

    /// <summary>
    /// Registers a stream handler and its closed-type pipeline executor.
    /// </summary>
    IMediatorServiceBuilder RegisterStreamHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage,
        TResponse>()
        where THandler : class, IStreamHandler<TMessage, TResponse>
        where TMessage : notnull;

    // ── Instance-based registration (useful for testing and dynamic scenarios) ──

    /// <summary>
    /// Registers a specific command handler instance and its closed-type pipeline executor.
    /// </summary>
    IMediatorServiceBuilder RegisterCommandHandler<TMessage>(ICommandHandler<TMessage> handler)
        where TMessage : notnull;

    /// <summary>
    /// Registers a specific notification handler instance and its closed-type pipeline executor.
    /// </summary>
    IMediatorServiceBuilder RegisterNotificationHandler<TMessage>(INotificationHandler<TMessage> handler)
        where TMessage : notnull;

    /// <summary>
    /// Registers a specific request handler instance and its closed-type pipeline executor.
    /// </summary>
    IMediatorServiceBuilder RegisterRequestHandler<TMessage, TResponse>(IRequestHandler<TMessage, TResponse> handler)
        where TMessage : notnull;

    /// <summary>
    /// Registers a specific stream handler instance and its closed-type pipeline executor.
    /// </summary>
    IMediatorServiceBuilder RegisterStreamHandler<TMessage, TResponse>(IStreamHandler<TMessage, TResponse> handler)
        where TMessage : notnull;

    /// <summary>
    /// Registers a pipeline behavior for a specific message type and result type.
    /// </summary>
    /// <typeparam name="TBehavior">The pipeline behavior type.</typeparam>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <returns>The current instance of <see cref="IMediatorServiceBuilder"/> for chaining.</returns>
    IMediatorServiceBuilder RegisterBehavior< // NOSONAR S2436
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior,
        TMessage,
        TResult>()
        where TBehavior : class, IPipelineBehavior<TMessage, TResult>
        where TMessage : notnull
        where TResult : notnull;
}