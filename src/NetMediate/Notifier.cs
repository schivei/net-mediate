using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NetMediate;

/// <inheritdoc/>
[Injectable(ServiceLifetime.Singleton, Order = int.MinValue)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class Notifier : INotifiable
{
    private readonly record struct HandlerState<TMessage>(
        INotificationHandler<TMessage> Handler,
        TMessage Message,
        CancellationToken CancellationToken
    );

    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public ValueTask DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0)
            return ValueTask.CompletedTask;

        foreach (var handler in handlers)
        {
            ThreadPool.QueueUserWorkItem(
                async static state =>
                {
                    try
                    {
                        await state.Handler.Handle(state.Message, state.CancellationToken).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Swallow exceptions to prevent unhandled exceptions from crashing the application.
                        // In a real-world application, consider logging the exception or handling it appropriately.
                    }
                },
                new HandlerState<TMessage>(handler, message, cancellationToken),
                preferLocal: false
            );
        }

        return ValueTask.CompletedTask;
    }
}
