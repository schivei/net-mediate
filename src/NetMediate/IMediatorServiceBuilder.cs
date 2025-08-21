using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate;

/// <summary>
/// Fluent builder for configuring mediator services: registering handlers, attaching per-handler predicates (filters),
/// customizing handler instantiation, and setting behavior for unhandled messages.
/// </summary>
public interface IMediatorServiceBuilder
{
    /// <summary>
    /// Gets the DI service collection being configured.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers a predicate that determines whether a specific command handler should execute
    /// for a given <typeparamref name="TMessage"/> instance.
    /// </summary>
    /// <typeparam name="TMessage">The command (message) type.</typeparam>
    /// <typeparam name="THandler">The handler type implementing <see cref="ICommandHandler{TMessage}"/>.</typeparam>
    /// <param name="filter">Predicate returning true to allow execution; false to skip.</param>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder FilterCommand<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, ICommandHandler<TMessage>;

    /// <summary>
    /// Registers a predicate that determines whether a specific notification handler should execute
    /// for a given <typeparamref name="TMessage"/> instance.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <typeparam name="THandler">The handler type implementing <see cref="INotificationHandler{TMessage}"/>.</typeparam>
    /// <param name="filter">Predicate returning true to allow execution; false to skip.</param>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder FilterNotification<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, INotificationHandler<TMessage>;

    /// <summary>
    /// Registers a predicate that determines whether a specific request handler should execute
    /// for a given <typeparamref name="TMessage"/> instance.
    /// </summary>
    /// <remarks>
    /// The handler constraint uses <c>object</c> as the response type; consider a generic overload if stronger typing is desired.
    /// </remarks>
    /// <typeparam name="TMessage">The request message type.</typeparam>
    /// <typeparam name="THandler">The handler type implementing <see cref="IRequestHandler{TRequest,TResponse}"/> for <typeparamref name="TMessage"/>.</typeparam>
    /// <param name="filter">Predicate returning true to allow execution; false to skip.</param>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder FilterRequest<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, IRequestHandler<TMessage, object>;

    /// <summary>
    /// Registers a predicate that determines whether a specific stream handler should execute
    /// for a given <typeparamref name="TMessage"/> instance.
    /// </summary>
    /// <remarks>
    /// The handler constraint uses <c>object</c> as the streamed item type; consider a generic overload if stronger typing is desired.
    /// </remarks>
    /// <typeparam name="TMessage">The streamed request message type.</typeparam>
    /// <typeparam name="THandler">The handler type implementing <see cref="IStreamHandler{TRequest,TItem}"/> for <typeparamref name="TMessage"/>.</typeparam>
    /// <param name="filter">Predicate returning true to allow execution; false to skip.</param>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder FilterStream<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, IStreamHandler<TMessage, object>;

    /// <summary>
    /// Configures how unhandled messages are treated.
    /// </summary>
    /// <param name="ignore">If true, absence of a handler does not throw.</param>
    /// <param name="log">If true, unhandled messages are logged.</param>
    /// <param name="logLevel">Log level used when <paramref name="log"/> is true.</param>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder IgnoreUnhandledMessages(
        bool ignore = true,
        bool log = true,
        LogLevel logLevel = LogLevel.Error
    );

    /// <summary>
    /// Registers a factory predicate that can dynamically select (instantiate) a handler type based on the message instance.
    /// </summary>
    /// <typeparam name="TMessage">The message type.</typeparam>
    /// <param name="filter">
    /// Function returning the handler <see cref="Type"/> that should process the supplied message, or null to indicate no dynamic handler.
    /// </param>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder InstantiateHandlerByMessageFilter<TMessage>(
        Func<TMessage, Type?> filter
    );

    /// <summary>
    /// Registers a notification handler for <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The notification message type.</typeparam>
    /// <typeparam name="THandler">The handler implementing <see cref="INotificationHandler{TMessage}"/>.</typeparam>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder RegisterNotificationHandler<TMessage, THandler>()
        where THandler : class, INotificationHandler<TMessage> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a command handler for <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The command message type.</typeparam>
    /// <typeparam name="THandler">The handler implementing <see cref="ICommandHandler{TMessage}"/>.</typeparam>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder RegisterCommandHandler<TMessage, THandler>()
        where THandler : class, ICommandHandler<TMessage> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a request handler for <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The request message type.</typeparam>
    /// <typeparam name="THandler">The handler implementing <see cref="IRequestHandler{TRequest,TResponse}"/> for <typeparamref name="TMessage"/>.</typeparam>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder RegisterRequestHandler<TMessage, THandler>()
        where THandler : class, IRequestHandler<TMessage, object> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a stream handler for <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The stream request message type.</typeparam>
    /// <typeparam name="THandler">The handler implementing <see cref="IStreamHandler{TRequest,TItem}"/> for <typeparamref name="TMessage"/>.</typeparam>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder RegisterStreamHandler<TMessage, THandler>()
        where THandler : class, IStreamHandler<TMessage, object> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a validation handler for <typeparamref name="TMessage"/>.
    /// </summary>
    /// <typeparam name="TMessage">The message type validated.</typeparam>
    /// <typeparam name="THandler">The handler implementing <see cref="IValidationHandler{TMessage}"/>.</typeparam>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder RegisterValidationHandler<TMessage, THandler>()
        where THandler : class, IValidationHandler<TMessage> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Core registration API mapping a message type to a handler type.
    /// </summary>
    /// <param name="messageType">The message type.</param>
    /// <param name="handlerType">The handler type.</param>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder Register(Type messageType, Type handlerType);
}
