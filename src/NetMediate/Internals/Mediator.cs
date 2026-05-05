using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal sealed class Mediator(
    IServiceProvider serviceProvider,
    INotifiable notifier
) : IMediator
{
    /// <inheritdoc/>
    public Task Notify<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) => Notify(null, message, cancellationToken);
    
    /// <inheritdoc/>
    public async Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        await notifier.Notify(key, message, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull => Notify(null, messages, cancellationToken);

    /// <inheritdoc/>
    public async Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        await notifier.Notify(key, messages, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task Send<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull => Send(null, message, cancellationToken);

    /// <inheritdoc/>
    public async Task Send<TMessage>(
        object? key,
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
            await pipeline.Handle(key, message, CommandHandlers, cancellationToken).ConfigureAwait(false);
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
    public Task Send<TMessage>(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull =>
        Send(null, messages, cancellationToken);

    /// <inheritdoc/>
    public async Task Send<TMessage>(object? key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        await Task.WhenAll(messages.Select(message => Send(key, message, cancellationToken)));
    }

    private static async Task CommandHandlers<TMessage>(
        object? _,
        TMessage message,
        IEnumerable<ICommandHandler<TMessage>> handlers,
        CancellationToken ct) where TMessage : notnull
    {
        foreach (var handler in handlers)
            await handler.Handle(message, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull => Request<TMessage, TResponse>(null, message, cancellationToken);

    /// <inheritdoc/>
    public async Task<TResponse> Request<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        var pipeline = serviceProvider
            .GetRequiredService<RequestPipelineExecutor<TMessage, TResponse>>();

        try
        {
            return await pipeline.Handle(key, message, RequestHandlers, cancellationToken).ConfigureAwait(false);
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

    private static Task<TResponse> RequestHandlers<TMessage, TResponse>(
        object? _,
        TMessage message,
        IRequestHandler<TMessage, TResponse>[] handlers,
        CancellationToken cancellationToken) where TMessage : notnull
    {
        return handlers.Single().Handle(message, cancellationToken);
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull => RequestStream<TMessage, TResponse>(null, message, cancellationToken);

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        var pipeline = serviceProvider
            .GetRequiredService<StreamPipelineExecutor<TMessage, TResponse>>();

        return pipeline.Handle(key, message, StreamHandlers, cancellationToken);
    }

    private static async IAsyncEnumerable<TResponse> StreamHandlers<TMessage, TResponse>(
        object? _,
        TMessage message,
        IStreamHandler<TMessage, TResponse>[] handlers,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TMessage : notnull
    {
        foreach (var handler in handlers)
            await foreach (var item in handler.Handle(message, cancellationToken).ConfigureAwait(false))
                yield return item;
    }
}
