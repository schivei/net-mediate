using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NetMediate.Internals;

internal class Mediator(ILogger<Mediator> logger, Configuration configuration, IServiceScopeFactory serviceScopeFactory) : IMediator, INotifiable
{

    public async Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default)
    {
        await ValidateMessage(message, cancellationToken);

        await configuration.ChannelWriter.WriteAsync(message, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ValidateMessage<TMessage>(TMessage message, CancellationToken cancellationToken) =>
        await configuration.ValidateMessageAsync(message, logger, Resolve<IValidationHandler<TMessage>>, cancellationToken);

    private bool AssertHandler<TMessage>(TMessage message, object handlers)
    {
        if (handlers is IEnumerable ien and not string && ien.Cast<object>().Any())
            return true;

        if (handlers is not null && (handlers is string or not IEnumerable))
            return true;

        if (!configuration.IgnoreUnhandledMessages)
            throw new InvalidOperationException($"No handler found for message type {typeof(TMessage).Name}");

        if (configuration.IgnoreUnhandledMessages && configuration.LogUnhandledMessages)
            logger.Log(configuration.UnhandledMessagesLogLevel, "No handler found for message type {MessageType}. Message: {Message}", typeof(TMessage).Name, message);

        return false;
    }

    public async Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default)
    {
        await ValidateMessage(message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}: {Message}", typeof(TMessage).Name, message);

        var handler = Resolve<ICommandHandler<TMessage>>(message).FirstOrDefault();

        if (!AssertHandler(message, handler))
            return;

        await handler.Handle(message, cancellationToken).ConfigureAwait(true);
    }

    public async Task<TResponse> Request<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default)
    {
        await ValidateMessage(message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}: {Message}", typeof(TMessage).Name, message);

        var handler = Resolve<IRequestHandler<TMessage, TResponse>>(message).FirstOrDefault();

        if (!AssertHandler(message, handler))
            return default!;

        return await handler.Handle(message, cancellationToken).ConfigureAwait(true);
    }

    public async IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ValidateMessage(message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}: {Message}", typeof(TMessage).Name, message);

        var handler = Resolve<IStreamHandler<TMessage, TResponse>>(message).FirstOrDefault();

        if (!AssertHandler(message, handler))
            yield break;

        await foreach (var response in handler.Handle(message, cancellationToken).ConfigureAwait(true))
        {
            yield return response;
        }
    }

    public Task Notifies(object message, CancellationToken cancellationToken = default) =>
        (Task)GetType().GetMethod(nameof(Notifies), BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(message.GetType())
            .Invoke(this, [message, cancellationToken]);

    private async Task Notifies<TMessage>(TMessage message, CancellationToken cancellationToken = default)
    {
        await ValidateMessage(message, cancellationToken);

        logger.LogDebug("Notifying message of type {MessageType}: {Message}", typeof(TMessage).Name, message);

        var handlers = Resolve<INotificationHandler<TMessage>>(message);

        if (!AssertHandler(message, handlers))
            return;

        await Task.WhenAll(handlers.Select(handler => handler.Handle(message, cancellationToken)))
            .ConfigureAwait(false);
    }

    private IEnumerable<T> Resolve<T>(object message, bool ignore = false)
    {
        var messageType = message.GetType();

        using var scope = serviceScopeFactory.CreateScope();

        var messageAttribute = messageType.GetCustomAttribute<KeyedMessageAttribute>(false);

        IEnumerable<T> handlers = [];
        try
        {
            handlers = messageAttribute is not null ?
                scope.ServiceProvider.GetKeyedServices<T>(messageAttribute.ServiceKey) :
                scope.ServiceProvider.GetServices<T>();
        }
        catch (InvalidOperationException ex)
        {
            handlers = ResolvesCatching(message, messageType, ex, ignore, handlers);
        }

        return FilterResolves(message, handlers);
    }

    [ExcludeFromCodeCoverage]
    private IEnumerable<T> FilterResolves<T>(object message, IEnumerable<T> handlers)
    {
        if (configuration.TryGetHandlerTypeByMessageFilter(message, out var type))
            return [handlers.First(h => h.GetType() == type)];

        return handlers;
    }

    [ExcludeFromCodeCoverage]
    private IEnumerable<T> ResolvesCatching<T>(object message, Type messageType, Exception ex, bool ignore, IEnumerable<T> handlers)
    {
        if (ignore)
            return [];

        if (!configuration.IgnoreUnhandledMessages)
            throw new InvalidOperationException($"No handler found for message type {messageType.Name}", ex);

        if (configuration.LogUnhandledMessages)
            logger.Log(configuration.UnhandledMessagesLogLevel, "No handler found for message type {MessageType}. Message: {Message}", messageType.Name, message);

        return handlers;
    }
}
