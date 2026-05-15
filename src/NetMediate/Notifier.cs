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
            try
            {
                var t = h.Handle(message, cancellationToken);
                if (t.IsFaulted)
                {
                    Observe(t.Exception);
                    continue;
                }

                if (!t.IsCompletedSuccessfully)
                {
                    _ = t.ContinueWith(
                        static task => Observe(task.Exception),
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted
                            | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default
                    );
                }
            }
            catch (Exception ex)
            {
                Observe(ex);
            }
        }

        return Task.CompletedTask;
    }

    private static void Observe(Exception? exception)
    {
        GC.KeepAlive(exception);
    }
}
