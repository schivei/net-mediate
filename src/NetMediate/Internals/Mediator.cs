using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

internal sealed class Mediator(
    Configuration configuration,
    IServiceScopeFactory serviceScopeFactory,
    INotifiable notifier
) : IMediator
{
    /// <inheritdoc/>
    public async ValueTask Notify<TMessage>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull, INotification
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");

        try
        {
            await notifier.Notify(
                message,
                cancellationToken
            ).ConfigureAwait(false);
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
    ) where TMessage : notnull, INotification
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Notify");

        try
        {
            await notifier.Notify(
                messages,
                cancellationToken
            ).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            NetMediateDiagnostics.RecordNotify<TMessage>(messages.Count());
        }
    }

    /// <inheritdoc/>
    public async ValueTask Send<TMessage>(
        TMessage command,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull, ICommand
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Send");
        using var scope = serviceScopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        try
        {
            var pipeline = MountPipeline<
               TMessage,
               ValueTask,
               CommandHandlerDelegate<TMessage>,
               ICommandHandler<TMessage>,
               ICommandBehavior<TMessage>>(
               serviceProvider,
               (validations, handlers) => async (message, token) =>
               {
                   foreach (var validation in validations)
                   {
                       await validation.ValidateAsync(message, token).ConfigureAwait(false);
                   }

                   await Task.WhenAll(handlers.Select(async handler => await handler.Handle(message, token).ConfigureAwait(false))).ConfigureAwait(false);
               },
               (behavior, next) => (message, token) => behavior.Handle(message, next, token)
            );

            if (pipeline is null)
            {
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

    public async ValueTask<TResponse> Request<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull, IRequest<TResponse>
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("Request");
        using var scope = serviceScopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        try
        {
            var pipeline = MountPipeline<
                TMessage,
                ValueTask<TResponse>,
                RequestHandlerDelegate<TMessage, TResponse>,
                IRequestHandler<TMessage, TResponse>,
                IRequestBehavior<TMessage, TResponse>>(
                serviceProvider,
                (validations, handlers) => (message, token) => new(Task.Run(async () =>
                {
                    foreach (var validation in validations)
                    {
                        await validation.ValidateAsync(message, token).ConfigureAwait(false);
                    }

                    return await handlers.First().Handle(message, token).ConfigureAwait(false);
                })),
                (behavior, next) => (message, token) => behavior.Handle(message, next, token)
            );

            if (pipeline is null)
            {
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

    public IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
        TMessage message,
        CancellationToken cancellationToken = default
    ) where TMessage : notnull, IStream<TResponse>
    {
        using var activity = NetMediateDiagnostics.StartActivity<TMessage>("RequestStream");
        using var scope = serviceScopeFactory.CreateScope();
        var serviceProvider = scope.ServiceProvider;
        IAsyncEnumerable<TResponse> stream = EmptyAsyncEnumerable<TResponse>();

        try
        {

            var pipeline = MountPipeline<
                TMessage,
                IAsyncEnumerable<TResponse>,
                StreamHandlerDelegate<TMessage, TResponse>,
                IStreamHandler<TMessage, TResponse>,
                IStreamBehavior<TMessage, TResponse>>(
                serviceProvider,
                (validations, handlers) => (message, token) => StreamRunnerAsync(validations, handlers, message, token),
                (behavior, next) => (message, token) => behavior.Handle(message, next, token)
            );

            if (pipeline is null)
            {
                return EmptyAsyncEnumerable<TResponse>();
            }

            return pipeline.Invoke(message, cancellationToken);
        }
        finally
        {
            NetMediateDiagnostics.RecordStream<TMessage>();
        }
    }

    private static async IAsyncEnumerable<TResponse> StreamRunnerAsync<TMessage, TResponse>(
        IEnumerable<IValidationHandler<TMessage>> validations,
        IEnumerable<IStreamHandler<TMessage, TResponse>> handlers,
        TMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken
    ) where TMessage : notnull, IStream<TResponse>
    {
        foreach (var validation in validations)
        {
            await validation.ValidateAsync(message, cancellationToken).ConfigureAwait(false);
        }

        foreach (var handler in handlers)
        {
            await foreach (var item in handler.Handle(message, cancellationToken).ConfigureAwait(false))
            {
                yield return item;
            }
        }
    }

    internal static TDelegate? MountPipeline<TMessage, TResult, TDelegate, THandler, TBehavior>(
        IServiceProvider serviceProvider,
        Func<IEnumerable<IValidationHandler<TMessage>>, IEnumerable<THandler>, TDelegate> createDelegate,
        Func<TBehavior, TDelegate, TDelegate> wrapBehavior
    ) where TMessage : notnull, IMessage where TDelegate : Delegate where THandler : IHandler<TMessage, TResult> where TBehavior : IPipelineBehavior<TMessage, TResult, TDelegate>
    {
        var handlers = serviceProvider.GetAllServices<THandler>();

        if (!handlers.Any())
            return null;

        var validations = serviceProvider.GetAllServices<IValidationHandler<TMessage>>();

        TDelegate next = createDelegate(validations, handlers);

        var behaviors = serviceProvider.GetAllServices<TBehavior>()
            .Reverse()
            .Select((behavior, i) => (behavior, i));

        foreach (var (behavior, i) in behaviors)
        {
            var current = next;
            next = wrapBehavior(behavior, current);
        }

        return next;
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        yield break;
    }

    internal IEnumerable<T> Resolve<T>(IServiceProvider serviceProvider)
    {
        var handlers = serviceProvider.GetAllServices<T>();

        if (!configuration.IgnoreUnhandledMessages && !handlers.Any())
        {
            throw new InvalidOperationException(
                $"No service registerd for {typeof(T)}"
            );
        }

        return handlers;
    }
}
