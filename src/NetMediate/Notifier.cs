using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace NetMediate;

/// <inheritdoc/>
[Injectable(ServiceLifetime.Singleton, Order = int.MinValue)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
internal sealed class Notifier : INotifiable
{
    /// <inheritdoc/>
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
            _ = handler.Handle(message, cancellationToken).AsTask()
                .ContinueWith(
                    static task =>
                    {
                        // ignore
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                );
        }

        return ValueTask.CompletedTask;
    }
}
