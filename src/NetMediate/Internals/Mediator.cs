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
        var pipeline = serviceProvider.GetService<CommandPipelineExecutor<TMessage>>();

        if (pipeline is null)
            return;

        try
        {
            await pipeline
                .Handle(
                    key,
                    message,
                    CommandHandlers,
                    cancellationToken
                )
                .ConfigureAwait(false);
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

    private static async Task CommandHandlers<TMessage>(
        object? _,
        TMessage message,
        IEnumerable<ICommandHandler<TMessage>> handlers,
        CancellationToken ct
    )
        where TMessage : notnull
    {
        foreach (var handler in handlers)
            await handler.Handle(message, ct).ConfigureAwait(false);
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
        var pipeline = serviceProvider.GetRequiredService<RequestPipelineExecutor<TMessage, TResponse>>();

        try
        {
            return await pipeline
                .Handle(
                    key,
                    message,
                    cancellationToken
                )
                .ConfigureAwait(false);
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
        var pipeline = serviceProvider.GetRequiredService<
            StreamPipelineExecutor<TMessage, TResponse>
        >();

        return pipeline.Handle(
            key,
            message,
            StreamHandlers,
            cancellationToken
        );
    }

    private static IAsyncEnumerable<TResponse> StreamHandlers<TMessage, TResponse>(
        object? _,
        TMessage message,
        IStreamHandler<TMessage, TResponse>[] handlers,
        CancellationToken cancellationToken
    )
        where TMessage : notnull
    {
        return handlers.Select(x => x.Handle(message, cancellationToken))
            .Aggregate((prev, next) => prev.Concat(next));
    }
}
