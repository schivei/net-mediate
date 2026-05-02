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
    /// Registers a handler for a specific message type and result type, with an optional factory for custom instantiation.
    /// </summary>
    /// <typeparam name="THandler">The type of the handler to register. Must implement <see cref="IHandler{TMessage, TResult}"/>.</typeparam>
    /// <typeparam name="TMessage">The type of message the handler will process. Must not be null.</typeparam>
    /// <typeparam name="TResult">The type of result the handler will return.</typeparam>
    /// <typeparam name="TInterface">The interface type that the handler implements. Must implement <see cref="IHandler{TMessage, TResult}"/>.</typeparam>
    /// <returns>The current instance of <see cref="IMediatorServiceBuilder"/> for chaining.</returns>
    IMediatorServiceBuilder RegisterHandler<
        TInterface,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler,
        TMessage,
        TResult>()
        where TInterface : class, IHandler<TMessage, TResult>
        where THandler : class, TInterface
        where TMessage : notnull
        where TResult : notnull;

    /// <summary>
    /// Registers a pipeline behavior for a specific message type and result type.
    /// </summary>
    /// <typeparam name="TBehavior">The type of the pipeline behavior to register. Must implement <see cref="IPipelineBehavior{TMessage, TResult}"/>.</typeparam>
    /// <typeparam name="TMessage">The type of message the pipeline behavior will process. Must not be null.</typeparam>
    /// <typeparam name="TResult">The type of result the pipeline behavior will return.</typeparam>
    /// <returns>The current instance of <see cref="IMediatorServiceBuilder"/> for chaining.</returns>
    IMediatorServiceBuilder RegisterBehavior<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TBehavior,
        TMessage,
        TResult>()
        where TBehavior : class, IPipelineBehavior<TMessage, TResult>
        where TMessage : notnull
        where TResult : notnull;
}