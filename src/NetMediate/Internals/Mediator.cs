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
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");
        try
        {
            await notifier.Notify(message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordNotify<TMessage>();
        }
    }

    /// <inheritdoc/>
    public async Task Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");

        try
        {
            await notifier.Notify(messages, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordNotify<TMessage>();
        }
    }

    /// <inheritdoc/>
    public async Task Send<TMessage>(
        TMessage command,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Send");

        try
        {
            var pipeline = serviceProvider
                .GetRequiredService<PipelineExecutor<TMessage, Task, ICommandHandler<TMessage>>>();

            await pipeline.Handle(command, CommandHandlers, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordSend<TMessage>();
        }
    }

    /// <inheritdoc/>
    public async Task Send<TMessage>(IEnumerable<TMessage> commands, CancellationToken cancellationToken = default) where TMessage : notnull
    {
        await Task.WhenAll(commands.Select(command => Send(command, cancellationToken)));
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
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");

        try
        {
            var pipeline = serviceProvider
                .GetRequiredService<RequestPipelineExecutor<TMessage, TResponse>>();

            return await pipeline.Handle(message, RequestHandlers, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordRequest<TMessage>();
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
        var activity = NetMediateDiagnostics.StartActivity<TMessage>("RequestStream");

        try
        {
            var pipeline = serviceProvider
                .GetRequiredService<StreamPipelineExecutor<TMessage, TResponse>>();

            return pipeline.Handle(message, StreamHandlers, cancellationToken);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordStream<TMessage>();
        }
    }

    private static IAsyncEnumerable<TResponse> StreamHandlers<TResponse, TMessage>(TMessage message, IStreamHandler<TMessage, TResponse>[] handlers, CancellationToken cancellationToken) where TMessage : notnull
    {
        return handlers.Single().Handle(message, cancellationToken);
    }
}