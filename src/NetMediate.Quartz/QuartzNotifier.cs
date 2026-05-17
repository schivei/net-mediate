using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Quartz;

/// <inheritdoc/>
[RequiresDynamicCode(
    "QuartzNotificationJob uses MakeGenericMethod for per-type notification dispatch and is not compatible with NativeAOT."
)]
[RequiresUnreferencedCode(
    "QuartzNotificationJob uses reflection to resolve message types by name and dispatch notifications."
)]
public sealed class QuartzNotifier : INotifiable
{
    /// <summary>
    /// Gets or sets the underlying <see cref="INotifiable"/> instance that this decorator wraps.
    /// </summary>
    /// <remarks>This property is required to fulfill the contract of <see cref="INotifiable"/> and enables
    /// the use of this class as a decorator. The property may not be used directly in typical scenarios.</remarks>
    [Inject] public required INotifiable Inner { get; init; }

    /// <summary>
    /// Gets the logger for QuartzNotifier operations.
    /// </summary>
    [Inject] public required ILogger<QuartzNotifier> Logger { get; init; }

    /// <inheritdoc />
    public async Task DispatchNotifications<TMessage>(
        object? key,
        TMessage message,
        INotificationHandler<TMessage>[] handlers,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        if (handlers.Length == 0 && Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug(
                "QuartzNotifier: no handlers registered for notification type {MessageType}.",
                typeof(TMessage).Name
            );
        }

        await Inner.DispatchNotifications(key, message, handlers, cancellationToken).ConfigureAwait(false);
    }
}
