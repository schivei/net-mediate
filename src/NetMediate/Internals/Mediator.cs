using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    private async Task ValidateMessage<TMessage>(TMessage message, CancellationToken cancellationToken)
    {
        if (!configuration.IgnoreUnhandledMessages)
            ArgumentNullException.ThrowIfNull(message);

        if (configuration.LogUnhandledMessages && message is null)
            logger.Log(configuration.UnhandledMessagesLogLevel, "Received null message. This may indicate a misconfiguration or an error in the message pipeline.");

        if (message is null)
            return;

        if (message is IValidatable validatable)
        {
            var validationResult = await validatable.ValidateAsync();
            if (validationResult.ErrorMessage is not null)
                throw new MessageValidationException(validationResult.ErrorMessage);
        }

        var handlers = Resolve<IValidationHandler<TMessage>>(message, true);

        if (handlers.Any())
        {
            foreach (var handler in handlers)
            {
                var validationResult = await handler.ValidateAsync(message, cancellationToken);
                if (validationResult.ErrorMessage is not null)
                    throw new MessageValidationException(validationResult.ErrorMessage);
            }
        }
    }

    public async Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default)
    {
        await ValidateMessage(message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}: {Message}", typeof(TMessage).Name, message);

        var handler = Resolve<ICommandHandler<TMessage>>(message).FirstOrDefault();

        if (handler is null)
        {
            if (!configuration.IgnoreUnhandledMessages)
                throw new InvalidOperationException($"No command handler found for message type {typeof(TMessage).Name}");

            if (configuration.IgnoreUnhandledMessages && configuration.LogUnhandledMessages)
                logger.Log(configuration.UnhandledMessagesLogLevel, "No command handler found for message type {MessageType}. Message: {Message}", typeof(TMessage).Name, message);

            return;
        }

        await handler.Handle(message, cancellationToken).ConfigureAwait(true);
    }

    public async Task<TResponse> Request<TMessage, TResponse>(TMessage message, CancellationToken cancellationToken = default)
    {
        await ValidateMessage(message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}: {Message}", typeof(TMessage).Name, message);

        var handler = Resolve<IRequestHandler<TMessage, TResponse>>(message).FirstOrDefault();

        if (handler is null)
        {
            if (!configuration.IgnoreUnhandledMessages)
                throw new InvalidOperationException($"No command handler found for message type {typeof(TMessage).Name}");

            if (configuration.IgnoreUnhandledMessages && configuration.LogUnhandledMessages)
                logger.Log(configuration.UnhandledMessagesLogLevel, "No command handler found for message type {MessageType}. Message: {Message}", typeof(TMessage).Name, message);

            return default!;
        }

        return await handler.Handle(message, cancellationToken).ConfigureAwait(true);
    }

    public async IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(TMessage message, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await ValidateMessage(message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}: {Message}", typeof(TMessage).Name, message);

        var handler = Resolve<IStreamHandler<TMessage, TResponse>>(message).FirstOrDefault();

        if (handler is null)
        {
            if (!configuration.IgnoreUnhandledMessages)
                throw new InvalidOperationException($"No command handler found for message type {typeof(TMessage).Name}");

            if (configuration.IgnoreUnhandledMessages && configuration.LogUnhandledMessages)
                logger.Log(configuration.UnhandledMessagesLogLevel, "No command handler found for message type {MessageType}. Message: {Message}", typeof(TMessage).Name, message);

            yield break;
        }

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

        if (!handlers.Any())
        {
            if (!configuration.IgnoreUnhandledMessages)
                throw new InvalidOperationException($"No notification handlers found for message type {typeof(TMessage).Name}");

            if (configuration.IgnoreUnhandledMessages && configuration.LogUnhandledMessages)
                logger.Log(configuration.UnhandledMessagesLogLevel, "No notification handlers found for message type {MessageType}. Message: {Message}", typeof(TMessage).Name, message);

            return;
        }

        await Task.WhenAll(handlers.Select(handler => handler.Handle(message, cancellationToken)))
            .ConfigureAwait(false);
    }

    private IEnumerable<T> Resolve<T>(object message, bool ignore = false)
    {
        if (message is null)
            return [];

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
            if (ignore)
                return [];

            if (!configuration.IgnoreUnhandledMessages)
                throw new InvalidOperationException($"No handler found for message type {messageType.Name}", ex);

            if (configuration.LogUnhandledMessages)
                logger.Log(configuration.UnhandledMessagesLogLevel, "No handler found for message type {MessageType}. Message: {Message}", messageType.Name, message);
        }

        if (configuration.TryGetHandlerTypeByMessageFilter(message, out var type))
            return [handlers.First(h => h.GetType() == type)];

        return handlers;
    }
}
