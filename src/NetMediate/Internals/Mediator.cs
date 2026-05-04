using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal sealed class Mediator(
    IServiceProvider serviceProvider,
    INotifiable notifier
) : IMediator
{
    /// <inheritdoc/>
    public async Task Notify<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        await notifier.Notify(message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        await notifier.Notify(messages, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task Send<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        // GetService (nullable) so that a message with no registered handler is a no-op
        // rather than throwing. Executors are only registered when a handler is registered
        // via RegisterCommandHandler<>.
        var pipeline = serviceProvider
            .GetService<PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>>();

        if (pipeline is null) return;

        try
        {
            await pipeline.Handle(message, CommandHandlers, cancellationToken).ConfigureAwait(false);
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
                ex);
        }
    }

    /// <inheritdoc/>
    public async Task Send<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        await Task.WhenAll(messages.Select(message => Send(message, cancellationToken)));
    }

    private static async Task CommandHandlers<TMessage>(TMessage message,
        IEnumerable<ICommandHandler<TMessage>> handlers,
        CancellationToken ct) where TMessage : notnull
    {
        foreach (var handler in handlers)
            await handler.Handle(message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        var pipeline = serviceProvider
            .GetRequiredService<RequestPipelineExecutor<TMessage, TResponse>>();

        try
        {
            return await pipeline.Handle(message, RequestHandlers, cancellationToken).ConfigureAwait(false);
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
                ex);
        }
    }

    private static Task<TResponse> RequestHandlers<TMessage, TResponse>(TMessage message, IRequestHandler<TMessage, TResponse>[] handlers, CancellationToken cancellationToken) where TMessage : notnull
    {
        return handlers.Single().Handle(message, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        var pipeline = serviceProvider
            .GetRequiredService<StreamPipelineExecutor<TMessage, TResponse>>();

        return pipeline.Handle(message, StreamHandlers, cancellationToken);
    }

    private static IAsyncEnumerable<TResponse> StreamHandlers<TResponse, TMessage>(TMessage message, IStreamHandler<TMessage, TResponse>[] handlers, CancellationToken cancellationToken) where TMessage : notnull
    {
        return handlers.Single().Handle(message, cancellationToken);
    }
}
