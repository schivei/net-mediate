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
                async static state => await state.Handler.Handle(state.Message, state.CancellationToken).ConfigureAwait(false),
                new HandlerState<TMessage>(handler, message, cancellationToken),
                preferLocal: false
            );
        }

        return ValueTask.CompletedTask;
    }
}
