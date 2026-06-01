using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace NetMediate;

/// <inheritdoc/>
[Injectable(ServiceLifetime.Transient, Order = int.MinValue)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class Mediator : IMediator
{
    [Inject] internal required INotifiable Notifier { get; init; }

    [Inject] internal required ICachedHandlers Cache { get; init; }

    private static async ValueTask DispatchCommandHandlersAsync<TMessage>(
        ImmutableArray<ICommandHandler<TMessage>> handlers,
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
        ImmutableArray<IStreamHandler<TMessage, TResponse>> handlers,
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
        ImmutableArray<IStreamHandler<TMessage, TResponse>> handlers,
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
    public void Notify<TMessage>(
        object? key,
        TMessage message
    ) where TMessage : notnull
    {
        var handlers = Cache.GetNotifyHandlers<TMessage>(key);

        _ = Notifier.DispatchNotifications(key, message, handlers);
    }

    /// <inheritdoc/>
    public void Notify<TMessage>(TMessage message) where TMessage : notnull =>
        Notify(null, message);

    /// <inheritdoc/>
    public void Notifies<TMessage>(
        IEnumerable<TMessage> messages
    ) where TMessage : notnull =>
        Notifies(null, messages);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public void Notifies<TMessage>(
        object? key,
        IEnumerable<TMessage> messages
    )
        where TMessage : notnull
    {
        if (!messages.Any())
            return;

        _ = Task.WhenAll(messages.Select(message => Task.Run(() => Notify(key, message))));
    }

    /// <inheritdoc/>
    public ValueTask Send<TMessage>(TMessage message, CancellationToken cancellationToken = default)
        where TMessage : notnull =>
        Send(null, message, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask Send<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        try
        {
            await DispatchCommandHandlersAsync(
                Cache.GetCommandHandlers<TMessage>(key),
                message,
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not MediatorException)
        {
            throw CreateMediatorException<TMessage>(typeof(ICommandHandler<TMessage>), ex);
        }
    }

    /// <inheritdoc/>
    public ValueTask Sends<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull =>
        Sends(null, messages, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public async ValueTask Sends<TMessage>(
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
    [ExcludeFromCodeCoverage]
    public ValueTask ParallelSends<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull => ParallelSends(null, messages, cancellationToken);

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public async ValueTask ParallelSends<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        if (!messages.Any())
            return;

        await Task.WhenAll(messages.Select(async message => await Send(key, message, cancellationToken)));
    }

    /// <inheritdoc/>
    public ValueTask<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull =>
        Request<TMessage, TResponse>(null, message, cancellationToken);

    /// <inheritdoc/>
    public async ValueTask<TResponse> Request<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        try
        {
            var handler = Cache.GetRequestHandler<TMessage, TResponse>(key);

            return await handler.Handle(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not MediatorException)
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
            Cache.GetStreamHandlers<TMessage, TResponse>(key),
            message,
            cancellationToken
        );
}
