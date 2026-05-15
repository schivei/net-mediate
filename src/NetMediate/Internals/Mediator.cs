using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

[Injectable<IMediator>]
internal sealed class Mediator(IServiceProvider serviceProvider, INotifiable notifier) : IMediator
{
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
                ? [.. serviceProvider.GetServices<ICommandHandler<TMessage>>()]
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
                ? serviceProvider.GetRequiredService<IRequestHandler<TMessage, TResponse>>()
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
            ? [.. serviceProvider.GetServices<IStreamHandler<TMessage, TResponse>>()]
            : [.. serviceProvider.GetKeyedServices<IStreamHandler<TMessage, TResponse>>(key)];

        if (handlers.Length == 0)
            return AsyncEnumerable.Empty<TResponse>();

        return handlers.Select(x => x.Handle(message, cancellationToken))
            .Aggregate((prev, next) => prev.Concat(next));
    }
}
