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
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");

        try
        {
            await configuration
                .ChannelWriter.WriteAsync(
                    new NotificationPacket<TMessage>(message, onError),
                    cancellationToken
                )
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordNotify<TMessage>();
        }
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
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Send");
        using var scope = serviceScopeFactory.CreateScope();

        try
        {
            await ValidateMessage(scope, message, cancellationToken);

            logger.LogDebug("Sending message of type {MessageType}", typeof(TMessage).Name);

            var handler = Resolve<ICommandHandler<TMessage>>(scope, message).FirstOrDefault();

            if (!AssertHandler<TMessage>(handler))
                return;

            await ExecuteCommandPipeline(scope, message, handler, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordSend<TMessage>();
        }
    }

    public async Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");
        using var scope = serviceScopeFactory.CreateScope();

        try
        {
            await ValidateMessage(scope, message, cancellationToken);

            logger.LogDebug("Sending message of type {MessageType}", typeof(TMessage).Name);

            var handler = Resolve<IRequestHandler<TMessage, TResponse>>(scope, message)
                .FirstOrDefault();

            if (!AssertHandler<TMessage>(handler))
                return default!;

            return await ExecuteRequestPipeline(scope, message, handler, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordRequest<TMessage>();
        }
    }

    public async IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("RequestStream");
        using var scope = serviceScopeFactory.CreateScope();
        IAsyncEnumerable<TResponse> stream = EmptyAsyncEnumerable<TResponse>();

        try
        {
            try
            {
                await ValidateMessage(scope, message, cancellationToken);

                logger.LogDebug("Sending message of type {MessageType}", typeof(TMessage).Name);

                var handler = Resolve<IStreamHandler<TMessage, TResponse>>(scope, message)
                    .FirstOrDefault();

                if (!AssertHandler<TMessage>(handler))
                    yield break;

                stream = ExecuteStreamPipeline(scope, message, handler, cancellationToken);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                throw;
            }

            await using var enumerator = stream.GetAsyncEnumerator(cancellationToken);

            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                    throw;
                }

                if (!hasNext)
                    break;

                yield return enumerator.Current;
            }
        }
        finally
        {
            NetMediateDiagnostics.RecordStream<TMessage>();
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

        await ExecuteNotificationPipeline(scope, packet, handlers, cancellationToken).ConfigureAwait(
            false
        );
    }

    private static async Task ExecuteCommandPipeline<TMessage>(
        IServiceScope scope,
        TMessage message,
        ICommandHandler<TMessage> handler,
        CancellationToken cancellationToken
    )
    {
        var behaviors = ResolveBehaviors<ICommandBehavior<TMessage>>(scope.ServiceProvider);
        CommandHandlerDelegate next = token => handler.Handle(message, token);

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = token => behavior.Handle(message, current, token);
        }

        await next(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<TResponse> ExecuteRequestPipeline<TMessage, TResponse>(
        IServiceScope scope,
        TMessage message,
        IRequestHandler<TMessage, TResponse> handler,
        CancellationToken cancellationToken
    )
    {
        var behaviors = ResolveBehaviors<IRequestBehavior<TMessage, TResponse>>(scope.ServiceProvider);
        RequestHandlerDelegate<TResponse> next = token => handler.Handle(message, token);

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = token => behavior.Handle(message, current, token);
        }

        return await next(cancellationToken).ConfigureAwait(false);
    }

    private static IAsyncEnumerable<TResponse> ExecuteStreamPipeline<TMessage, TResponse>(
        IServiceScope scope,
        TMessage message,
        IStreamHandler<TMessage, TResponse> handler,
        CancellationToken cancellationToken
    )
    {
        var behaviors = ResolveBehaviors<IStreamBehavior<TMessage, TResponse>>(scope.ServiceProvider);
        StreamHandlerDelegate<TResponse> next = token => handler.Handle(message, token);

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = token => behavior.Handle(message, current, token);
        }

        return next(cancellationToken);
    }

    private static async Task ExecuteNotificationPipeline<TMessage>(
        IServiceScope scope,
        NotificationPacket<TMessage> packet,
        IEnumerable<INotificationHandler<TMessage>> handlers,
        CancellationToken cancellationToken
    )
    {
        var behaviors = ResolveBehaviors<INotificationBehavior<TMessage>>(scope.ServiceProvider);
        var shouldPropagateExceptions = behaviors.Length > 0;
        NotificationHandlerDelegate next = async token =>
        {
            List<Exception>? exceptions = null;
            object? exceptionSync = null;
            var tasks = handlers.Select(async handler =>
            {
                try
                {
                    await handler.Handle(packet.Message, token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await packet.OnErrorAsync(handler.GetType(), ex).ConfigureAwait(false);
                    var sync = LazyInitializer.EnsureInitialized(ref exceptionSync, static () => new object());
                    lock (sync)
                    {
                        exceptions ??= [];
                        exceptions.Add(ex);
                    }
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);

            if (
                shouldPropagateExceptions
                && exceptions is not null
                && exceptions.Count > 0
            )
                throw new AggregateException(exceptions);
        };

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = token => behavior.Handle(packet.Message, current, token);
        }

        await next(cancellationToken).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        yield break;
    }

    private static TBehavior[] ResolveBehaviors<TBehavior>(IServiceProvider serviceProvider) =>
        // Fast path for the common case where no behaviors are registered for this message flow.
        // This avoids per-call IEnumerable resolution/allocation in high-throughput paths.
        serviceProvider is IServiceProviderIsService isService
            && !isService.IsService(typeof(TBehavior))
            && !isService.IsService(typeof(IEnumerable<TBehavior>))
            ? []
            : serviceProvider.GetService<IEnumerable<TBehavior>>()?.ToArray() ?? [];

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

    Task IMediator.Notify<TMessage>(INotification<TMessage> notification, NotificationErrorDelegate<TMessage> onError, CancellationToken cancellationToken) =>
        Notify((TMessage)notification, onError, cancellationToken);

    Task IMediator.Notify<TMessage>(IEnumerable<TMessage> messages, NotificationErrorDelegate<TMessage> onError, CancellationToken cancellationToken)
    {
        if (messages is null || !messages.Any())
            return Task.CompletedTask;

        return Task.WhenAll(messages.Select(message => Notify(message, onError, cancellationToken)));
    }

    Task IMediator.Notify<TMessage>(IEnumerable<INotification<TMessage>> notifications, NotificationErrorDelegate<TMessage> onError, CancellationToken cancellationToken)
    {
        if (notifications is null || !notifications.Any())
            return Task.CompletedTask;

        return Task.WhenAll(notifications.Select(notification => Notify((TMessage)notification, onError, cancellationToken)));
    }

    Task IMediator.Notify<TMessage>(TMessage message, CancellationToken cancellationToken) =>
        Notify(message, (_, _, _) => Task.CompletedTask, cancellationToken);

    Task IMediator.Notify<TMessage>(INotification<TMessage> notification, CancellationToken cancellationToken) =>
        Notify((TMessage)notification, (_, _, _) => Task.CompletedTask, cancellationToken);

    Task IMediator.Notify<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken)
    {
        if (messages is null || !messages.Any())
            return Task.CompletedTask;

        return Task.WhenAll(messages.Select(message => Notify(message, (_, _, _) => Task.CompletedTask, cancellationToken)));
    }

    Task IMediator.Notify<TMessage>(IEnumerable<INotification<TMessage>> notifications, CancellationToken cancellationToken)
    {
        if (notifications is null || !notifications.Any())
            return Task.CompletedTask;

        return Task.WhenAll(notifications.Select(notification => Notify((TMessage)notification, (_, _, _) => Task.CompletedTask, cancellationToken)));
    }

    Task IMediator.Send<TMessage>(ICommand<TMessage> command, CancellationToken cancellationToken) =>
        Send((TMessage)command, cancellationToken);

    Task<TResponse> IMediator.Request<TMessage, TResponse>(IRequest<TMessage, TResponse> request, CancellationToken cancellationToken) =>
        Request<TMessage, TResponse>((TMessage)request, cancellationToken);

    IAsyncEnumerable<TResponse> IMediator.RequestStream<TMessage, TResponse>(IStream<TMessage, TResponse> request, CancellationToken cancellationToken) =>
        RequestStream<TMessage, TResponse>((TMessage)request, cancellationToken);
}
