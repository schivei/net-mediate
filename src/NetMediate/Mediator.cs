using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace NetMediate;

/// <inheritdoc/>
[Injectable<IMediator>(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class Mediator : IMediator
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
            ICommandHandler<TMessage>[] handlers = ResolveCommandHandlers<TMessage>(key);

            foreach (var handler in handlers)
                await handler.Handle(message, cancellationToken).ConfigureAwait(false);
        }
        catch (MediatorException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new MediatorException(
                typeof(TMessage),
                typeof(ICommandHandler<TMessage>),
                Activity.Current?.Id,
                ex
            );
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
            throw new MediatorException(
                typeof(TMessage),
                typeof(IRequestHandler<TMessage, TResponse>),
                Activity.Current?.Id,
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
    {
        IStreamHandler<TMessage, TResponse>[] handlers = ResolveStreamHandlers<TMessage, TResponse>(key);

        if (handlers.Length == 0)
            return AsyncEnumerable.Empty<TResponse>();

        if (handlers.Length == 1)
            return handlers[0].Handle(message, cancellationToken);

        var stream = handlers[0].Handle(message, cancellationToken);
        for (int i = 1; i < handlers.Length; i++)
            stream = stream.Concat(handlers[i].Handle(message, cancellationToken));

        return stream;
    }
}
