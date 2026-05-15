using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel;

namespace NetMediate;

/// <inheritdoc/>
[Injectable<INotifiable>(ServiceLifetime.Singleton, ThreadIsolation = ThreadIsolationPolicy.None, Order = int.MinValue)]
[Browsable(false)]
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class Notifier : INotifiable
{
    /// <summary>
    /// Gets the service provider for resolving dependencies.
    /// </summary>
    [Inject] public required IServiceProvider ServiceProvider { get; init; }

    /// <inheritdoc/>
    public Task DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0)
            return Task.CompletedTask;

        foreach (var h in handlers)
        {
            var t = h.Handle(message, cancellationToken);
            if (!t.IsCompletedSuccessfully)
                _ = t.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        return Task.CompletedTask;
    }
}
