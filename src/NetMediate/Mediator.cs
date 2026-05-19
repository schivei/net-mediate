using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace NetMediate;

/// <inheritdoc/>
[Injectable(ServiceLifetime.Singleton, Order = int.MinValue)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class Mediator : IMediator
{
    /// <summary>
    /// Gets the service provider for resolving dependencies.
    /// </summary>
    [Inject] public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>
    /// Gets the notification dispatcher responsible for dispatching notifications to registered handlers.
    /// </summary>
    [Inject] public required INotifiable Notifier { get; init; }

    private readonly ConcurrentDictionary<Type, object> _cmdCache = new();
    private readonly ConcurrentDictionary<Type, object> _ntfCache = new();
    private readonly ConcurrentDictionary<(Type, Type), object> _reqCache = new();
    private readonly ConcurrentDictionary<(Type, Type), object> _streamCache = new();

    private ICommandHandler<TMessage>[] GetCommandHandlers<TMessage>()
        where TMessage : notnull =>
        (ICommandHandler<TMessage>[])_cmdCache.GetOrAdd(
            typeof(TMessage),
            _ => ServiceProvider.GetServices<ICommandHandler<TMessage>>().ToArray());

    private INotificationHandler<TMessage>[] GetNotifyHandlers<TMessage>()
        where TMessage : notnull =>
        (INotificationHandler<TMessage>[])_ntfCache.GetOrAdd(
            typeof(TMessage),
            _ => ServiceProvider.GetServices<INotificationHandler<TMessage>>().ToArray());

    private IRequestHandler<TMessage, TResponse> GetRequestHandler<TMessage, TResponse>()
        where TMessage : notnull =>
        (IRequestHandler<TMessage, TResponse>)_reqCache.GetOrAdd(
            (typeof(TMessage), typeof(TResponse)),
            _ => ServiceProvider.GetRequiredService<IRequestHandler<TMessage, TResponse>>());

    private IStreamHandler<TMessage, TResponse>[] GetStreamHandlers<TMessage, TResponse>()
        where TMessage : notnull =>
        (IStreamHandler<TMessage, TResponse>[])_streamCache.GetOrAdd(
            (typeof(TMessage), typeof(TResponse)),
            _ => ServiceProvider.GetServices<IStreamHandler<TMessage, TResponse>>().ToArray());

    private ICommandHandler<TMessage>[] ResolveCommandHandlers<TMessage>(object? key)
        where TMessage : notnull =>
        key is null
            ? GetCommandHandlers<TMessage>()
            : [.. ServiceProvider.GetKeyedServices<ICommandHandler<TMessage>>(key)];

    private INotificationHandler<TMessage>[] ResolveNotifyHandlers<TMessage>(object? key)
        where TMessage : notnull =>
        key is null
            ? GetNotifyHandlers<TMessage>()
            : [.. ServiceProvider.GetKeyedServices<INotificationHandler<TMessage>>(key)];

    private IRequestHandler<TMessage, TResponse> ResolveRequestHandler<TMessage, TResponse>(object? key)
        where TMessage : notnull =>
        key is null
            ? GetRequestHandler<TMessage, TResponse>()
            : ServiceProvider.GetRequiredKeyedService<IRequestHandler<TMessage, TResponse>>(key);

    private IStreamHandler<TMessage, TResponse>[] ResolveStreamHandlers<TMessage, TResponse>(object? key)
        where TMessage : notnull =>
        key is null
            ? GetStreamHandlers<TMessage, TResponse>()
            : [.. ServiceProvider.GetKeyedServices<IStreamHandler<TMessage, TResponse>>(key)];

    private static async Task DispatchCommandHandlersAsync<TMessage>(
        ICommandHandler<TMessage>[] handlers,
        TMessage message,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        foreach (var handler in handlers)
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
    }

    private static MediatorException CreateMediatorException<TMessage>(
        Type handlerType,
        Exception exception
    )
        where TMessage : notnull =>
        new(
            typeof(TMessage),
            handlerType,
            Activity.Current?.Id,
            exception
        );

    private static IAsyncEnumerable<TResponse> BuildStreamDispatch<TMessage, TResponse>(
        IStreamHandler<TMessage, TResponse>[] handlers,
        TMessage message,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0)
            return AsyncEnumerable.Empty<TResponse>();

        if (handlers.Length == 1)
            return handlers[0].Handle(message, cancellationToken);

        return ConcatStreams(handlers, message, cancellationToken);
    }

    private static IAsyncEnumerable<TResponse> ConcatStreams<TMessage, TResponse>(
        IStreamHandler<TMessage, TResponse>[] handlers,
        TMessage message,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        var stream = handlers[0].Handle(message, cancellationToken);
        for (int i = 1; i < handlers.Length; i++)
            stream = stream.Concat(handlers[i].Handle(message, cancellationToken));

        return stream;
    }

    /// <inheritdoc/>
    public Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) =>
        Notify(null, message, cancellationToken);

    /// <inheritdoc/>
    public Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        INotificationHandler<TMessage>[] handlers = ResolveNotifyHandlers<TMessage>(key);

        _ = Notifier.DispatchNotifications(key, message, handlers, cancellationToken);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull =>
        Notify(null, messages, cancellationToken);

    /// <inheritdoc/>
    public Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (!messages.Any())
            return Task.CompletedTask;

        foreach (var m in messages)
            Notify(key, m, cancellationToken);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task Send<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull =>
        Send(null, message, cancellationToken);

    /// <inheritdoc/>
    public async Task Send<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        try
        {
            await DispatchCommandHandlersAsync(
                ResolveCommandHandlers<TMessage>(key),
                message,
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch (MediatorException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateMediatorException<TMessage>(typeof(ICommandHandler<TMessage>), ex);
        }
    }

    /// <inheritdoc/>
    public Task Send<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull =>
        Send(null, messages, cancellationToken);

    /// <inheritdoc/>
    public async Task Send<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (!messages.Any())
            return;

        foreach (var sender in messages)
        {
            await Send(key, sender, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull =>
        Request<TMessage, TResponse>(null, message, cancellationToken);

    /// <inheritdoc/>
    public async Task<TResponse> Request<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        try
        {
            var handler = ResolveRequestHandler<TMessage, TResponse>(key);

            return await handler.Handle(message, cancellationToken).ConfigureAwait(false);
        }
        catch (MediatorException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw CreateMediatorException<TMessage>(
                typeof(IRequestHandler<TMessage, TResponse>),
                ex
            );
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull =>
        RequestStream<TMessage, TResponse>(
            null,
            message,
            cancellationToken
        );

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
        => BuildStreamDispatch(
            ResolveStreamHandlers<TMessage, TResponse>(key),
            message,
            cancellationToken
        );
}
