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
    public async Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        if (serviceProvider is not IKeyedServiceProvider keyed) return;

        foreach (var handler in keyed.GetKeyedServices<INotificationHandler<TMessage>>(key))
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public async Task Send<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        if (serviceProvider is not IKeyedServiceProvider keyed) return;

        foreach (var handler in keyed.GetKeyedServices<ICommandHandler<TMessage>>(key))
            await handler.Handle(message, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public Task<TResponse> Request<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        if (serviceProvider is not IKeyedServiceProvider keyed)
            throw new InvalidOperationException(
                "The current service provider does not support keyed services.");

        return keyed.GetRequiredKeyedService<IRequestHandler<TMessage, TResponse>>(key)
            .Handle(message, cancellationToken);
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

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        if (serviceProvider is not IKeyedServiceProvider keyed)
            return EmptyStream<TResponse>();

        return KeyedStreamHandlers<TMessage, TResponse>(keyed, key, message, cancellationToken);
    }

    private static async IAsyncEnumerable<TResponse> KeyedStreamHandlers<TMessage, TResponse>(
        IKeyedServiceProvider keyed,
        object? key,
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TMessage : notnull
    {
        foreach (var handler in keyed.GetKeyedServices<IStreamHandler<TMessage, TResponse>>(key))
            await foreach (var item in handler.Handle(message, cancellationToken).ConfigureAwait(false))
                yield return item;
    }

    private static async IAsyncEnumerable<TResponse> StreamHandlers<TMessage, TResponse>(
        TMessage message,
        IStreamHandler<TMessage, TResponse>[] handlers,
        [EnumeratorCancellation] CancellationToken cancellationToken)
        where TMessage : notnull
    {
        foreach (var handler in handlers)
            await foreach (var item in handler.Handle(message, cancellationToken).ConfigureAwait(false))
                yield return item;
    }

    private static IAsyncEnumerable<T> EmptyStream<T>()
    {
        return GetAsync();
        static async IAsyncEnumerable<T> GetAsync()
        {
            await Task.Yield();
            yield break;
        }
    }
}
