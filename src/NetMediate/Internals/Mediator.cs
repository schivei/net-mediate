using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal class Mediator(
    ILogger<Mediator> logger,
    Configuration configuration,
    IServiceScopeFactory serviceScopeFactory
) : IMediator, INotifiable
{
    public async Task Notify<TMessage>(
        TMessage message,
        NotificationErrorDelegate<TMessage> onError,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = serviceScopeFactory.CreateScope();

        await configuration
            .ChannelWriter.WriteAsync(
                new NotificationPacket<TMessage>(message, onError),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    private async Task ValidateMessage<TMessage>(
        IServiceScope scope,
        TMessage message,
        CancellationToken cancellationToken
    ) =>
        await configuration.ValidateMessageAsync(
            scope,
            message,
            logger,
            Resolve<IValidationHandler<TMessage>>,
            cancellationToken
        );

    private bool AssertHandler<TMessage, THandler>(IEnumerable<THandler> handlers)
        where THandler : IHandler
    {
        if (handlers is not null && handlers.Any() && handlers.All(o => o is not null))
            return true;

        return AssertHandler<TMessage>(handlers.FirstOrDefault());
    }

    private bool AssertHandler<TMessage>(IHandler? handler)
    {
        if (handler is not null)
            return true;

        if (!configuration.IgnoreUnhandledMessages)
            throw new InvalidOperationException(
                $"No handler found for message type {typeof(TMessage).Name}"
            );

        if (configuration.IgnoreUnhandledMessages && configuration.LogUnhandledMessages)
            logger.Log(
                configuration.UnhandledMessagesLogLevel,
                "No handler found for message type {MessageType}.",
                typeof(TMessage).Name
            );

        return false;
    }

    public async Task Send<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = serviceScopeFactory.CreateScope();

        await ValidateMessage(scope, message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}", typeof(TMessage).Name);

        var handler = Resolve<ICommandHandler<TMessage>>(scope, message).FirstOrDefault();

        if (!AssertHandler<TMessage>(handler))
            return;

        await handler.Handle(message, cancellationToken).ConfigureAwait(true);
    }

    public async Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = serviceScopeFactory.CreateScope();

        await ValidateMessage(scope, message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}", typeof(TMessage).Name);

        var handler = Resolve<IRequestHandler<TMessage, TResponse>>(scope, message)
            .FirstOrDefault();

        if (!AssertHandler<TMessage>(handler))
            return default!;

        return await handler.Handle(message, cancellationToken).ConfigureAwait(true);
    }

    public async IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var scope = serviceScopeFactory.CreateScope();

        await ValidateMessage(scope, message, cancellationToken);

        logger.LogDebug("Sending message of type {MessageType}", typeof(TMessage).Name);

        var handler = Resolve<IStreamHandler<TMessage, TResponse>>(scope, message).FirstOrDefault();

        if (!AssertHandler<TMessage>(handler))
            yield break;

        await foreach (
            var response in handler.Handle(message, cancellationToken).ConfigureAwait(true)
        )
        {
            yield return response;
        }
    }

    public Task Notifies(
        INotificationPacket packet,
        CancellationToken cancellationToken = default
    ) =>
        (Task)
            GetType()
                .GetMethod(nameof(Notifies), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(packet.Message.GetType())
                .Invoke(this, [packet, cancellationToken]);

    private async Task Notifies<TMessage>(
        NotificationPacket<TMessage> packet,
        CancellationToken cancellationToken = default
    )
    {
        using var scope = serviceScopeFactory.CreateScope();

        await ValidateMessage(scope, packet.Message, cancellationToken);

        logger.LogDebug("Notifying message of type {MessageType}", typeof(TMessage).Name);

        var handlers = Resolve<INotificationHandler<TMessage>>(scope, packet.Message);

        if (!AssertHandler<TMessage, INotificationHandler<TMessage>>(handlers))
            return;

        var tasks = handlers.Select(async handler =>
        {
            try
            {
                await handler.Handle(packet.Message, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                await packet.OnErrorAsync(handler.GetType(), ex).ConfigureAwait(false);
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private IEnumerable<T> Resolve<T>(IServiceScope scope, object message, bool ignore = false)
    {
        logger.LogDebug(
            "Resolving handlers for message of type {MessageType}",
            message.GetType().Name
        );

        var messageType = message.GetType();

        var messageAttribute = messageType.GetCustomAttribute<KeyedMessageAttribute>(false);

        IEnumerable<T> handlers = [];
        try
        {
            handlers = messageAttribute is not null
                ? scope.ServiceProvider.GetKeyedServices<T>(messageAttribute.ServiceKey)
                : scope.ServiceProvider.GetServices<T>();
        }
        catch (InvalidOperationException ex)
        {
            handlers = ResolvesCatching(messageType, ex, ignore, handlers);
        }

        handlers = FilterResolves(message, handlers);

        logger.LogDebug(
            "Resolved {HandlerCount} handlers for message of type {MessageType}",
            handlers.Count(),
            messageType.Name
        );

        return handlers;
    }

    [ExcludeFromCodeCoverage]
    private IEnumerable<T> FilterResolves<T>(object message, IEnumerable<T> handlers)
    {
        logger.LogDebug(
            "Filtering handlers for message of type {MessageType}",
            message.GetType().Name
        );

        if (configuration.TryGetHandlerTypeByMessageFilter(message, out var type))
        {
            return [handlers.First(h => h.GetType() == type)];
        }

        return handlers;
    }

    [ExcludeFromCodeCoverage]
    private IEnumerable<T> ResolvesCatching<T>(
        Type messageType,
        Exception ex,
        bool ignore,
        IEnumerable<T> handlers
    )
    {
        if (ignore)
            return [];

        if (!configuration.IgnoreUnhandledMessages)
            throw new InvalidOperationException(
                $"No handler found for message type {messageType.Name}",
                ex
            );

        if (configuration.LogUnhandledMessages)
            logger.Log(
                configuration.UnhandledMessagesLogLevel,
                "No handler found for message type {MessageType}.",
                messageType.Name
            );

        return handlers;
    }
}
