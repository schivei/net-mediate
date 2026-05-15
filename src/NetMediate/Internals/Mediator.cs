using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

[Injectable<IMediator>]
internal sealed class Mediator(IServiceProvider serviceProvider, INotifiable notifier) : IMediator
{
    // Handler caches — populated once per handler type, on first dispatch.
    // Cache scope is per-Mediator instance/provider to avoid cross-container contamination
    // between test suites and multi-tenant hosts.
    private readonly ConcurrentDictionary<Type, object> _cmdCache    = new();
    private readonly ConcurrentDictionary<(Type, Type), object> _reqCache    = new();
    private readonly ConcurrentDictionary<(Type, Type), object> _streamCache = new();

    private ICommandHandler<TMessage>[] GetCommandHandlers<TMessage>()
        where TMessage : notnull =>
        (ICommandHandler<TMessage>[])_cmdCache.GetOrAdd(
            typeof(TMessage),
            _ => (object)serviceProvider.GetServices<ICommandHandler<TMessage>>().ToArray());

    private IRequestHandler<TMessage, TResponse> GetRequestHandler<TMessage, TResponse>()
        where TMessage : notnull =>
        (IRequestHandler<TMessage, TResponse>)_reqCache.GetOrAdd(
            (typeof(TMessage), typeof(TResponse)),
            _ => (object)serviceProvider.GetRequiredService<IRequestHandler<TMessage, TResponse>>());

    private IStreamHandler<TMessage, TResponse>[] GetStreamHandlers<TMessage, TResponse>()
        where TMessage : notnull =>
        (IStreamHandler<TMessage, TResponse>[])_streamCache.GetOrAdd(
            (typeof(TMessage), typeof(TResponse)),
            _ => (object)serviceProvider.GetServices<IStreamHandler<TMessage, TResponse>>().ToArray());

    /// <inheritdoc/>
    public Task Notify<TMessage>(TMessage message, CancellationToken cancellationToken = default) =>
        Notify(null, message, cancellationToken);

    /// <inheritdoc/>
    public Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    ) => notifier.Notify(key, message, cancellationToken);

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
        where TMessage : notnull =>
        notifier.Notify(key, messages, cancellationToken);

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
            ICommandHandler<TMessage>[] handlers = key is null
                ? GetCommandHandlers<TMessage>()
                : [.. serviceProvider.GetKeyedServices<ICommandHandler<TMessage>>(key)];

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
            var handler = key is null
                ? GetRequestHandler<TMessage, TResponse>()
                : serviceProvider.GetRequiredKeyedService<IRequestHandler<TMessage, TResponse>>(key);

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
        IStreamHandler<TMessage, TResponse>[] handlers = key is null
            ? GetStreamHandlers<TMessage, TResponse>()
            : [.. serviceProvider.GetKeyedServices<IStreamHandler<TMessage, TResponse>>(key)];

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
