using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate.Internals;

internal sealed class Mediator(
    Configuration configuration,
    IServiceScopeFactory serviceScopeFactory,
    INotifiable notifier,
    ILogger<Mediator> logger
) : IMediator
{
    /// <inheritdoc/>
    public async ValueTask Notify<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");
        try
        {
            await NotifyCore(message, cancellationToken).ConfigureAwait(false);
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
    public async ValueTask Notify<TMessage>(
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");
        // Materialize once to avoid double-enumeration
        var messageList = messages.ToList();
        try
        {
            await NotifyCore(messageList, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordNotify<TMessage>(messageList.Count);
        }
    }

    /// <inheritdoc/>
    public async ValueTask Send<TMessage>(
        TMessage command,
        CancellationToken cancellationToken = default
    )
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Send");
        using var scope = serviceScopeFactory.CreateScope();

        try
        {
            var pipeline = MountPipeline<
               TMessage, ValueTask,
               CommandHandlerDelegate<TMessage>,
               ICommandHandler<TMessage>,
               ICommandBehavior<TMessage>>(
               scope.ServiceProvider,
               (validations, handlers) => async (msg, token) =>
               {
                   await ValidateMessageAsync(msg, validations, token).ConfigureAwait(false);
                   await Task.WhenAll(handlers.Select(h => h.Handle(msg, token).AsTask())).ConfigureAwait(false);
               },
               (behavior, next) => (msg, token) => behavior.Handle(msg, next, token)
            );

            if (pipeline is null)
            {
                HandleMissingPipeline<TMessage>();
                return;
            }

            await pipeline.Invoke(command, cancellationToken).ConfigureAwait(false);
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
    public async ValueTask<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull, IRequest<TResponse>
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");
        using var scope = serviceScopeFactory.CreateScope();

        try
        {
            var pipeline = MountPipeline<
                TMessage, ValueTask<TResponse>,
                RequestHandlerDelegate<TMessage, TResponse>,
                IRequestHandler<TMessage, TResponse>,
                IRequestBehavior<TMessage, TResponse>>(
                scope.ServiceProvider,
                (validations, handlers) => async (msg, token) =>
                {
                    await ValidateMessageAsync(msg, validations, token).ConfigureAwait(false);
                    var handler = handlers.FirstOrDefault();
                    if (handler is null)
                        throw new InvalidOperationException($"No handler found for message type '{typeof(TMessage).Name}'.");
                    return await handler.Handle(msg, token).ConfigureAwait(false);
                },
                (behavior, next) => (msg, token) => behavior.Handle(msg, next, token)
            );

            if (pipeline is null)
            {
                HandleMissingPipeline<TMessage>();
                // When IgnoreUnhandledMessages is true, return default value for TResponse
                // This may be null for reference types, but is expected behavior for missing handlers
                return default!;
            }

            return await pipeline.Invoke(message, cancellationToken).ConfigureAwait(false);
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

    /// <inheritdoc/>
    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull, IStream<TResponse>
    {
        // Activity and scope cannot be disposed here because they must remain alive
        // until the async enumerable is fully consumed by the caller
        var activity = NetMediateDiagnostics.StartActivity<TMessage>("RequestStream");
        var scope = serviceScopeFactory.CreateScope();

        try
        {
            var pipeline = MountPipeline<
                TMessage, IAsyncEnumerable<TResponse>,
                StreamHandlerDelegate<TMessage, TResponse>,
                IStreamHandler<TMessage, TResponse>,
                IStreamBehavior<TMessage, TResponse>>(
                scope.ServiceProvider,
                (validations, handlers) => (msg, token) => StreamRunnerAsync(validations, handlers, msg, token),
                (behavior, next) => (msg, token) => behavior.Handle(msg, next, token)
            );

            if (pipeline is null)
            {
                activity?.Dispose();
                scope.Dispose();
                HandleMissingPipeline<TMessage>();
                return EmptyAsyncEnumerable<TResponse>();
            }

            NetMediateDiagnostics.RecordStream<TMessage>();
            return StreamWithCleanup(pipeline.Invoke(message, cancellationToken), activity, scope);
        }
        catch
        {
            activity?.Dispose();
            scope.Dispose();
            throw;
        }
    }

    private static async IAsyncEnumerable<TResponse> StreamWithCleanup<TResponse>(
        IAsyncEnumerable<TResponse> source,
        System.Diagnostics.Activity? activity,
        IServiceScope scope,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        try
        {
            await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            activity?.Dispose();
            scope.Dispose();
        }
    }

    private async ValueTask NotifyCore<TMessage>(TMessage message, CancellationToken cancellationToken)
    {   
        var handlers = scope.ServiceProvider.GetAllServices<INotificationHandler<TMessage>>();

        if (!handlers.Any())
        {
            HandleMissingPipeline<TMessage>();
            return;
        }

        await notifier.Notify(message, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask NotifyCore<TMessage>(IList<TMessage> messages, CancellationToken cancellationToken)
        where TMessage : notnull, INotification
    {
        using var scope = serviceScopeFactory.CreateScope();
        var handlers = scope.ServiceProvider.GetAllServices<INotificationHandler<TMessage>>();

        if (!handlers.Any())
        {
            HandleMissingPipeline<TMessage>();
            return;
        }

        await notifier.Notify(messages, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Throws <see cref="InvalidOperationException"/> when ignore is off; otherwise logs a warning.</summary>
    private void HandleMissingPipeline<TMessage>()
    {
        if (!configuration.IgnoreUnhandledMessages)
            throw new InvalidOperationException(
                $"No handler found for message type '{typeof(TMessage).Name}'.");

        logger.LogWarning("No handler found for message type '{MessageType}'.", typeof(TMessage).Name);
    }

    internal static TDelegate? MountPipeline<TMessage, TResult, TDelegate, THandler, TBehavior>(
        IServiceProvider serviceProvider,
        Func<IEnumerable<IValidationHandler<TMessage>>, IEnumerable<THandler>, TDelegate> createDelegate,
        Func<TBehavior, TDelegate, TDelegate> wrapBehavior
    ) where TDelegate : Delegate
      where THandler : IHandler<TMessage, TResult>
      where TBehavior : IPipelineBehavior<TMessage, TResult, TDelegate>
    {
        var handlers = serviceProvider.GetAllServices<THandler>();

        if (!handlers.Any())
            return null;

        var validations = serviceProvider.GetAllServices<IValidationHandler<TMessage>>();

        TDelegate next = createDelegate(validations, handlers);

        foreach (var behavior in serviceProvider.GetAllServices<TBehavior>().Reverse())
        {
            var current = next;
            next = wrapBehavior(behavior, current);
        }

        return next;
    }

    /// <summary>
    /// Runs IValidatable self-validation and then all registered IValidationHandler results,
    /// throwing <see cref="MessageValidationException"/> on the first failure.
    /// </summary>
    internal static async ValueTask ValidateMessageAsync<TMessage>(
        TMessage message,
        IEnumerable<IValidationHandler<TMessage>> validations,
        CancellationToken cancellationToken
    ) where TMessage : notnull, IMessage
    {
        if (message is IValidatable selfValidatable)
        {
            var selfResult = await selfValidatable.ValidateAsync().ConfigureAwait(false);
            if (selfResult != System.ComponentModel.DataAnnotations.ValidationResult.Success)
                throw new MessageValidationException(selfResult);
        }

        foreach (var v in validations)
        {
            var result = await v.ValidateAsync(message, cancellationToken).ConfigureAwait(false);
            if (result != System.ComponentModel.DataAnnotations.ValidationResult.Success)
                throw new MessageValidationException(result);
        }
    }

    private static async IAsyncEnumerable<TResponse> StreamRunnerAsync<TMessage, TResponse>(
        IEnumerable<IValidationHandler<TMessage>> validations,
        IEnumerable<IStreamHandler<TMessage, TResponse>> handlers,
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken
    ) where TMessage : notnull, IStream<TResponse>
    {
        await ValidateMessageAsync(message, validations, cancellationToken).ConfigureAwait(false);

        foreach (var handler in handlers)
            await foreach (var item in handler.Handle(message, cancellationToken).ConfigureAwait(false))
                yield return item;
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        yield break;
    }
}

