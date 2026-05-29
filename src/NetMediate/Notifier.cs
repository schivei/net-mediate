using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
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
    public Task DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        ImmutableArray<INotificationHandler<TMessage>> handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0)
            return Task.CompletedTask;

        _ = Task.WhenAll(
            handlers.Select(handler =>
                handler.Handle(message, cancellationToken)
                .ContinueWith(
                    ByPass,
                    CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default
                )
            )
        );

        return Task.CompletedTask;
    }

    [ExcludeFromCodeCoverage]
    private static void ByPass(Task _)
    {
        // ignore
    }
}
