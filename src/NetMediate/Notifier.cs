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

        var tasks = handlers.Select(handler =>
             Task.Run(async () => await handler.Handle(message, cancellationToken).ConfigureAwait(false), cancellationToken)
             .ContinueWith(ByPass, cancellationToken)
            );

        _ = Task.WhenAll(tasks);

        return ValueTask.CompletedTask;
    }

    [ExcludeFromCodeCoverage]
    private static void ByPass(Task _)
    {
        // no-op
    }
}
