using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal class Mediator(
    ILogger<Mediator> logger,
    Configuration configuration,
    IServiceProvider serviceProvider,
    IServiceScopeFactory serviceScopeFactory,
    INotificationProvider notificationProvider
) : IMediator, INotifiable, INotificationDispatcher
{
    // ── Cached type names (avoids repeated .Name allocations in log calls) ────
    private static readonly ConcurrentDictionary<Type, string> s_typeNames = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string GetTypeName<TMessage>() =>
        s_typeNames.GetOrAdd(typeof(TMessage), static t => t.Name);

    // ── Notification publish (IMediator.Notify) ───────────────────────────────
    public async Task Notify<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = configuration.EnableTelemetry
            ? NetMediateDiagnostics.StartActivity<TMessage>("Notify")
            : null;

        try
        {
            await notificationProvider
                .EnqueueAsync(message, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            if (configuration.EnableTelemetry)
                NetMediateDiagnostics.RecordNotify<TMessage>();
        }
    }

    // ── INotificationDispatcher ───────────────────────────────────────────────
    public Task DispatchAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default) =>
        NotifiesTyped(new NotificationPacket<TMessage>(message), cancellationToken);

    // ── Validation ────────────────────────────────────────────────────────────
    private async Task ValidateMessage<TMessage>(
        IServiceProvider sp,
        TMessage message,
        CancellationToken cancellationToken
    )
    {
        if (!configuration.EnableValidation)
            return;

        if (!configuration.NeedsValidation<TMessage>())
            return;

        await configuration.ValidateMessageAsync(
            sp,
            message,
            logger,
            Resolve<IValidationHandler<TMessage>>,
            cancellationToken
        );
    }

    // ── Handler assertion ─────────────────────────────────────────────────────
    private bool AssertHandler<TMessage, THandler>(THandler[] handlers)
        where THandler : IHandler
    {
        if (handlers.Length > 0)
            return true;

        return AssertHandler<TMessage>(default(THandler));
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

    // ── Behavior check (lazy scope creation) ─────────────────────────────────
    /// <summary>
    /// Returns <see langword="true"/> when behaviors of <typeparamref name="TBehavior"/> are
    /// registered.  When the provider does not expose <see cref="IServiceProviderIsService"/>
    /// (e.g. test mocks) it conservatively returns <see langword="true"/> so the mock's setup
    /// is honoured.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasBehaviors<TBehavior>() =>
        serviceProvider is not IServiceProviderIsService iss
        || iss.IsService(typeof(TBehavior))
        || iss.IsService(typeof(IEnumerable<TBehavior>));

    // ── Send (command) ────────────────────────────────────────────────────────
    public async Task Send<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = configuration.EnableTelemetry
            ? NetMediateDiagnostics.StartActivity<TMessage>("Send")
            : null;

        try
        {
            await ValidateMessage(serviceProvider, message, cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Sending message of type {MessageType}", GetTypeName<TMessage>());

            var handlers = Resolve<ICommandHandler<TMessage>>(serviceProvider, message);

            if (!AssertHandler<TMessage>(handlers.Length > 0 ? handlers[0] : null))
                return;

            var handler = handlers[0];

            if (HasBehaviors<ICommandBehavior<TMessage>>())
            {
                using var scope = serviceScopeFactory.CreateScope();
                await ExecuteCommandPipeline(
                    scope.ServiceProvider,
                    message,
                    handler,
                    cancellationToken
                ).ConfigureAwait(false);
            }
            else
            {
                await handler.Handle(message, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            if (configuration.EnableTelemetry)
                NetMediateDiagnostics.RecordSend<TMessage>();
        }
    }

    // ── Request ───────────────────────────────────────────────────────────────
    public async Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = configuration.EnableTelemetry
            ? NetMediateDiagnostics.StartActivity<TMessage>("Request")
            : null;

        try
        {
            await ValidateMessage(serviceProvider, message, cancellationToken);

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Sending message of type {MessageType}", GetTypeName<TMessage>());

            var handlers = Resolve<IRequestHandler<TMessage, TResponse>>(
                serviceProvider,
                message
            );

            if (!AssertHandler<TMessage>(handlers.Length > 0 ? handlers[0] : null))
                return default!;

            var handler = handlers[0];

            if (HasBehaviors<IRequestBehavior<TMessage, TResponse>>())
            {
                using var scope = serviceScopeFactory.CreateScope();
                return await ExecuteRequestPipeline(
                    scope.ServiceProvider,
                    message,
                    handler,
                    cancellationToken
                ).ConfigureAwait(false);
            }

            return await handler.Handle(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            if (configuration.EnableTelemetry)
                NetMediateDiagnostics.RecordRequest<TMessage>();
        }
    }

    // ── RequestStream ─────────────────────────────────────────────────────────
    public async IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var activity = configuration.EnableTelemetry
            ? NetMediateDiagnostics.StartActivity<TMessage>("RequestStream")
            : null;

        IAsyncEnumerable<TResponse>? stream = null;
        // Scope is only created when behaviors are registered; kept alive for entire enumeration.
        IServiceScope? behaviorScope = null;

        try
        {
            try
            {
                await ValidateMessage(serviceProvider, message, cancellationToken);

                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug(
                        "Sending message of type {MessageType}",
                        GetTypeName<TMessage>()
                    );

                var handlers = Resolve<IStreamHandler<TMessage, TResponse>>(
                    serviceProvider,
                    message
                );

                if (!AssertHandler<TMessage>(handlers.Length > 0 ? handlers[0] : null))
                    yield break;

                var handler = handlers[0];

                if (HasBehaviors<IStreamBehavior<TMessage, TResponse>>())
                {
                    behaviorScope = serviceScopeFactory.CreateScope();
                    stream = ExecuteStreamPipeline(
                        behaviorScope.ServiceProvider,
                        message,
                        handler,
                        cancellationToken
                    );
                }
                else
                {
                    stream = handler.Handle(message, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                throw;
            }

            await using var enumerator = stream!.GetAsyncEnumerator(cancellationToken);

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
            if (configuration.EnableTelemetry)
                NetMediateDiagnostics.RecordStream<TMessage>();

            behaviorScope?.Dispose();
        }
    }

    // ── INotifiable (called by NotificationWorker) ────────────────────────────
    public Task Notifies(
        INotificationPacket packet,
        CancellationToken cancellationToken = default
    ) => packet.DispatchAsync(this, cancellationToken);

    public async Task NotifiesTyped<TMessage>(
        NotificationPacket<TMessage> packet,
        CancellationToken cancellationToken = default
    )
    {
        await ValidateMessage(serviceProvider, packet.Message, cancellationToken);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Notifying message of type {MessageType}", GetTypeName<TMessage>());

        var handlers = Resolve<INotificationHandler<TMessage>>(serviceProvider, packet.Message);

        if (!AssertHandler<TMessage, INotificationHandler<TMessage>>(handlers))
            return;

        if (HasBehaviors<INotificationBehavior<TMessage>>())
        {
            using var scope = serviceScopeFactory.CreateScope();
            await ExecuteNotificationPipeline(
                scope.ServiceProvider,
                packet,
                handlers,
                cancellationToken
            ).ConfigureAwait(false);
        }
        else
        {
            await ExecuteNotificationPipeline(
                serviceProvider,
                packet,
                handlers,
                cancellationToken
            ).ConfigureAwait(false);
        }
    }

    // ── Pipeline helpers ──────────────────────────────────────────────────────
    private static async Task ExecuteCommandPipeline<TMessage>(
        IServiceProvider behaviorProvider,
        TMessage message,
        ICommandHandler<TMessage> handler,
        CancellationToken ct
    )
    {
        var behaviors = ResolveBehaviors<ICommandBehavior<TMessage>>(behaviorProvider);
        if (behaviors.Length == 0)
        {
            await handler.Handle(message, ct).ConfigureAwait(false);
            return;
        }

        CommandHandlerDelegate next = token => handler.Handle(message, token);
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = token => behavior.Handle(message, current, token);
        }

        await next(ct).ConfigureAwait(false);
    }

    private static async Task<TResponse> ExecuteRequestPipeline<TMessage, TResponse>(
        IServiceProvider behaviorProvider,
        TMessage message,
        IRequestHandler<TMessage, TResponse> handler,
        CancellationToken ct
    )
    {
        var behaviors = ResolveBehaviors<IRequestBehavior<TMessage, TResponse>>(behaviorProvider);
        if (behaviors.Length == 0)
            return await handler.Handle(message, ct).ConfigureAwait(false);

        RequestHandlerDelegate<TResponse> next = token => handler.Handle(message, token);
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = token => behavior.Handle(message, current, token);
        }

        return await next(ct).ConfigureAwait(false);
    }

    private static IAsyncEnumerable<TResponse> ExecuteStreamPipeline<TMessage, TResponse>(
        IServiceProvider behaviorProvider,
        TMessage message,
        IStreamHandler<TMessage, TResponse> handler,
        CancellationToken ct
    )
    {
        var behaviors = ResolveBehaviors<IStreamBehavior<TMessage, TResponse>>(behaviorProvider);
        if (behaviors.Length == 0)
            return handler.Handle(message, ct);

        StreamHandlerDelegate<TResponse> next = token => handler.Handle(message, token);
        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = token => behavior.Handle(message, current, token);
        }

        return next(ct);
    }

    private static async Task ExecuteNotificationPipeline<TMessage>(
        IServiceProvider behaviorProvider,
        NotificationPacket<TMessage> packet,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken ct
    )
    {
        var behaviors = ResolveBehaviors<INotificationBehavior<TMessage>>(behaviorProvider);
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
                    var sync = LazyInitializer.EnsureInitialized(
                        ref exceptionSync,
                        static () => new object()
                    );
                    lock (sync)
                    {
                        exceptions ??= [];
                        exceptions.Add(ex);
                    }
                }
            });
            await Task.WhenAll(tasks).ConfigureAwait(false);

            // Always propagate handler exceptions in cascade (same behaviour as MediatR).
            if (exceptions is { Count: > 0 })
                throw new AggregateException(exceptions);
        };

        for (var i = behaviors.Length - 1; i >= 0; i--)
        {
            var behavior = behaviors[i];
            var current = next;
            next = token => behavior.Handle(packet.Message, current, token);
        }

        await next(ct).ConfigureAwait(false);
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        yield break;
    }

    private static TBehavior[] ResolveBehaviors<TBehavior>(IServiceProvider serviceProvider) =>
        serviceProvider is IServiceProviderIsService isService
            && !isService.IsService(typeof(TBehavior))
            && !isService.IsService(typeof(IEnumerable<TBehavior>))
            ? []
            : serviceProvider.GetService<IEnumerable<TBehavior>>()?.ToArray() ?? [];

    // ── Service-key cache ─────────────────────────────────────────────────────
    private static readonly ConcurrentDictionary<Type, string?> s_serviceKeyCache = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string? GetServiceKey(Type messageType) =>
        s_serviceKeyCache.GetOrAdd(
            messageType,
            static t => t.GetCustomAttribute<KeyedMessageAttribute>(false)?.ServiceKey
        );

    // ── Resolve ───────────────────────────────────────────────────────────────
    private T[] Resolve<T>(IServiceProvider sp, object message, bool ignore = false)
    {
        var messageType = message.GetType();

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "Resolving handlers for message of type {MessageType}",
                s_typeNames.GetOrAdd(messageType, static t => t.Name)
            );

        var serviceKey = GetServiceKey(messageType);

        T[] handlers;
        try
        {
            handlers = serviceKey is not null
                ? sp.GetKeyedServices<T>(serviceKey).ToArray()
                : sp.GetServices<T>().ToArray();
        }
        catch (InvalidOperationException ex)
        {
            return ResolvesCatching<T>(messageType, ex, ignore) ?? [];
        }

        handlers = FilterResolves(message, handlers);

        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "Resolved {HandlerCount} handlers for message of type {MessageType}",
                handlers.Length,
                s_typeNames.GetOrAdd(messageType, static t => t.Name)
            );

        return handlers;
    }

    [ExcludeFromCodeCoverage]
    private T[] FilterResolves<T>(object message, T[] handlers)
    {
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug(
                "Filtering handlers for message of type {MessageType}",
                s_typeNames.GetOrAdd(message.GetType(), static t => t.Name)
            );

        if (configuration.TryGetHandlerTypeByMessageFilter(message, out var type))
            return [handlers.First(h => h.GetType() == type)];

        return handlers;
    }

    [ExcludeFromCodeCoverage]
    private T[]? ResolvesCatching<T>(Type messageType, Exception ex, bool ignore)
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

        return null;
    }

    // ── IMediator explicit implementations (netstandard2.0 + DIM fallbacks) ──
    Task IMediator.Notify<TMessage>(
        INotification<TMessage> notification,
        CancellationToken cancellationToken
    ) => Notify((TMessage)notification, cancellationToken);

    Task IMediator.Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken
    )
    {
        if (messages is null)
            return Task.CompletedTask;
        var buffered = messages as TMessage[] ?? messages.ToArray();
        if (buffered.Length == 0)
            return Task.CompletedTask;
        return Task.WhenAll(buffered.Select(m => Notify(m, cancellationToken)));
    }

    Task IMediator.Notify<TMessage>(
        IEnumerable<INotification<TMessage>> notifications,
        CancellationToken cancellationToken
    )
    {
        if (notifications is null)
            return Task.CompletedTask;
        var buffered =
            notifications as INotification<TMessage>[] ?? notifications.ToArray();
        if (buffered.Length == 0)
            return Task.CompletedTask;
        return Task.WhenAll(buffered.Select(n => Notify((TMessage)n, cancellationToken)));
    }

    Task IMediator.Send<TMessage>(
        ICommand<TMessage> command,
        CancellationToken cancellationToken
    ) => Send((TMessage)command, cancellationToken);

    Task<TResponse> IMediator.Request<TMessage, TResponse>(
        IRequest<TMessage, TResponse> request,
        CancellationToken cancellationToken
    ) => Request<TMessage, TResponse>((TMessage)request, cancellationToken);

    IAsyncEnumerable<TResponse> IMediator.RequestStream<TMessage, TResponse>(
        IStream<TMessage, TResponse> request,
        CancellationToken cancellationToken
    ) => RequestStream<TMessage, TResponse>((TMessage)request, cancellationToken);
}
