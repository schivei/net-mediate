using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate;

/// <summary>
/// Provides a builder interface for configuring mediator services and message handler filters.
/// </summary>
public interface IMediatorServiceBuilder
{
    /// <summary>
    /// Gets the service collection used for dependency injection.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Adds a filter for a specific command handler type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the command message.</typeparam>
    /// <typeparam name="THandler">The type of the command handler.</typeparam>
    /// <param name="filter">A function to determine if the handler should process the message.</param>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder FilterCommand<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, ICommandHandler<TMessage>;

    /// <summary>
    /// Adds a filter for a specific notification handler type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the notification message.</typeparam>
    /// <typeparam name="THandler">The type of the notification handler.</typeparam>
    /// <param name="filter">A function to determine if the handler should process the message.</param>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder FilterNotification<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, INotificationHandler<TMessage>;

    /// <summary>
    /// Adds a filter for a specific request handler type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the request message.</typeparam>
    /// <typeparam name="THandler">The type of the request handler.</typeparam>
    /// <param name="filter">A function to determine if the handler should process the message.</param>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder FilterRequest<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, IRequestHandler<TMessage, object>;

    /// <summary>
    /// Adds a filter for a specific stream handler type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the stream message.</typeparam>
    /// <typeparam name="THandler">The type of the stream handler.</typeparam>
    /// <param name="filter">A function to determine if the handler should process the message.</param>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder FilterStream<TMessage, THandler>(Func<TMessage, bool> filter)
        where THandler : class, IStreamHandler<TMessage, object>;

    /// <summary>
    /// Configures the behavior for unhandled messages.
    /// </summary>
    /// <param name="ignore">If true, unhandled messages will be ignored.</param>
    /// <param name="log">If true, unhandled messages will be logged.</param>
    /// <param name="logLevel">The log level to use for unhandled messages.</param>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder IgnoreUnhandledMessages(
        bool ignore = true,
        bool log = true,
        LogLevel logLevel = LogLevel.Error
    );

    /// <summary>
    /// Registers a filter to instantiate a handler type based on the message instance.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message.</typeparam>
    /// <param name="filter">A function that returns the handler type for the given message, or null if none.</param>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder InstantiateHandlerByMessageFilter<TMessage>(
        Func<TMessage, Type?> filter
    );

    /// <summary>
    /// Registers a notification handler for a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to validate.</typeparam>
    /// <typeparam name="THandler">The type of the validation handler.</typeparam>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder RegisterNotificationHandler<TMessage, THandler>()
        where THandler : class, INotificationHandler<TMessage> =>
    /// Registers a notification handler for a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to handle.</typeparam>
    /// <typeparam name="THandler">The type of the notification handler.</typeparam>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder RegisterNotificationHandler<TMessage, THandler>()
        where THandler : class, INotificationHandler<TMessage> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a command handler for a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to validate.</typeparam>
    /// <typeparam name="THandler">The type of the validation handler.</typeparam>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder RegisterCommandHandler<TMessage, THandler>()
        where THandler : class, ICommandHandler<TMessage> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a request handler for a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to validate.</typeparam>
    /// <typeparam name="THandler">The type of the validation handler.</typeparam>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder RegisterRequestHandler<TMessage, THandler>()
        where THandler : class, IRequestHandler<TMessage, object> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a stream handler for a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to validate.</typeparam>
    /// <typeparam name="THandler">The type of the validation handler.</typeparam>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder RegisterStreamHandler<TMessage, THandler>()
        where THandler : class, IStreamHandler<TMessage, object> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a validation handler for a specific message type.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to validate.</typeparam>
    /// <typeparam name="THandler">The type of the validation handler.</typeparam>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder RegisterValidationHandler<TMessage, THandler>()
        where THandler : class, IValidationHandler<TMessage> =>
        Register(typeof(TMessage), typeof(THandler));

    /// <summary>
    /// Registers a message type with its corresponding handler type in the mediator service builder.
    /// </summary>
    /// <param name="messageType">A type representing the message to be handled.</param>
    /// <param name="handlerType">A type representing the handler for the message.</param>
    /// <returns>The <see cref="IMediatorServiceBuilder"/> instance for chaining.</returns>
    IMediatorServiceBuilder Register(Type messageType, Type handlerType);
}
