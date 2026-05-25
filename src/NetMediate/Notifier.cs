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
    /// <inheritdoc/>
    [ExcludeFromCodeCoverage]
    public async ValueTask DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0)
            return;

        foreach (var handler in handlers)
        {
            try
            {
                await handler.Handle(message, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // ignore
            }
        }
    }
}
