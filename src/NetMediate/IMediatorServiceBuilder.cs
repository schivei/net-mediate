using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate;

#pragma warning disable S2436
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
    /// Gets the collection of service descriptors for dependency injection configuration.
    /// </summary>
    /// <remarks>Use this property to register application services, configure dependencies, or modify the
    /// service collection before building the service provider. The returned collection is typically used during
    /// application startup or module initialization.</remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers a handler for a specific message type and result type.
    /// </summary>
    /// <typeparam name="TInterface">The interface type that the handler implements.</typeparam>
    /// <typeparam name="THandler">The concrete handler type.</typeparam>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <param name="key">An optional key to distinguish this handler from others of the same interface type.</param>
    /// <returns>The current instance of <see cref="IMediatorServiceBuilder"/> for chaining.</returns>
    IMediatorServiceBuilder RegisterHandler<
        TInterface,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage,
        TResult
    >(object? key = null)
        where TInterface : class, IHandler<TMessage, TResult>
        where THandler : class, TInterface
        where TMessage : notnull
        where TResult : notnull;

    /// <summary>
    /// Registers a command handler and its closed-type pipeline executor.
    /// Prefer this over <see cref="RegisterHandler{TInterface,THandler,TMessage,TResult}"/> for commands.
    /// </summary>
    /// <param name="key">An optional key to distinguish this handler from others of the same interface type.</param>
    IMediatorServiceBuilder RegisterCommandHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage
    >(object? key = null)
        where THandler : class, ICommandHandler<TMessage>
        where TMessage : notnull;

    /// <summary>
    /// Registers a notification handler and its closed-type pipeline executor.
    /// </summary>
    /// <param name="key">An optional key to distinguish this handler from others of the same interface type.</param>
    IMediatorServiceBuilder RegisterNotificationHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage
    >(object? key = null)
        where THandler : class, INotificationHandler<TMessage>
        where TMessage : notnull;

    /// <summary>
    /// Registers a request handler and its closed-type pipeline executor.
    /// </summary>
    /// <param name="key">An optional key to distinguish this handler from others of the same interface type.</param>
    IMediatorServiceBuilder RegisterRequestHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage,
        TResponse
    >(object? key = null)
        where THandler : class, IRequestHandler<TMessage, TResponse>
        where TMessage : notnull;

    /// <summary>
    /// Registers a stream handler and its closed-type pipeline executor.
    /// </summary>
    /// <param name="key">An optional key to distinguish this handler from others of the same interface type.</param>
    IMediatorServiceBuilder RegisterStreamHandler<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage,
        TResponse
    >(object? key = null)
        where THandler : class, IStreamHandler<TMessage, TResponse>
        where TMessage : notnull;

    /// <summary>
    /// Registers a pipeline behavior for a specific message type and result type.
    /// </summary>
    /// <typeparam name="TBehavior">The pipeline behavior type.</typeparam>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <typeparam name="TResult">The result type.</typeparam>
    /// <returns>The current instance of <see cref="IMediatorServiceBuilder"/> for chaining.</returns>
    IMediatorServiceBuilder RegisterBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior,
        TMessage,
        TResult
    >()
        where TBehavior : class, IPipelineBehavior<TMessage, TResult>
        where TMessage : notnull
        where TResult : notnull;

    /// <summary>
    /// Registers a notification-specific pipeline behavior.
    /// Provides a symmetric registration experience to
    /// <see cref="RegisterBehavior{TBehavior,TMessage,TResult}"/> for notification pipelines.
    /// </summary>
    /// <typeparam name="TBehavior">The <see cref="IPipelineNotificationBehavior{TMessage}"/> implementation type.</typeparam>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <returns>The current instance of <see cref="IMediatorServiceBuilder"/> for chaining.</returns>
    IMediatorServiceBuilder RegisterNotificationBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior,
        TMessage
    >()
        where TBehavior : class, IPipelineNotificationBehavior<TMessage>
        where TMessage : notnull;
}
#pragma warning restore S2436
