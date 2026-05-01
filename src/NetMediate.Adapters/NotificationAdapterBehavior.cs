using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace NetMediate.Adapters;

/// <summary>
/// A NetMediate notification pipeline behavior that forwards notifications to all registered
/// <see cref="INotificationAdapter{TMessage}"/> implementations after the core handlers have run.
/// </summary>
/// <remarks>
/// <para>
/// This behavior sits at the outer edge of the notification pipeline. It calls <c>next</c> first (running
/// validation and all <see cref="INotificationHandler{TMessage}"/> implementations), then forwards the
/// message to every registered adapter wrapped in an <see cref="AdapterEnvelope{TMessage}"/>.
/// </para>
/// <para>
/// Whether adapter invocations are sequential or parallel and whether failures propagate is controlled by
/// <see cref="NotificationAdapterOptions"/>.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The notification message type.</typeparam>
public sealed class NotificationAdapterBehavior<TMessage>(
    IServiceProvider serviceProvider,
    NotificationAdapterOptions options,
    ILogger<NotificationAdapterBehavior<TMessage>> logger
) : INotificationBehavior<TMessage> where TMessage : notnull, INotification
{
    /// <inheritdoc />
    public async ValueTask Handle(
        TMessage message,
        NotificationHandlerDelegate<TMessage> next,
        CancellationToken cancellationToken = default)
    {
        // Execute the core pipeline first.
        await next(message, cancellationToken).ConfigureAwait(false);

        // Then forward to external adapters.
        var adapters = serviceProvider.GetServices<INotificationAdapter<TMessage>>().ToList();
        if (adapters.Count == 0)
            return;

        var envelope = AdapterEnvelope<TMessage>.Create(message);

        if (options.InvokeAdaptersInParallel)
        {
            await InvokeParallelAsync(adapters, envelope, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await InvokeSequentialAsync(adapters, envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeSequentialAsync(
        IEnumerable<INotificationAdapter<TMessage>> adapters,
        AdapterEnvelope<TMessage> envelope,
        CancellationToken cancellationToken)
    {
        foreach (var adapter in adapters)
        {
            await InvokeAdapterAsync(adapter, envelope, cancellationToken).ConfigureAwait(false);
        }
    }

    private async ValueTask InvokeParallelAsync(
        IEnumerable<INotificationAdapter<TMessage>> adapters,
        AdapterEnvelope<TMessage> envelope,
        CancellationToken cancellationToken)
    {
        var tasks = adapters
            .Select(a => InvokeAdapterAsync(a, envelope, cancellationToken).AsTask());

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async ValueTask InvokeAdapterAsync(
        INotificationAdapter<TMessage> adapter,
        AdapterEnvelope<TMessage> envelope,
        CancellationToken cancellationToken)
    {
        try
        {
            await adapter.ForwardAsync(envelope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (!options.ThrowOnAdapterFailure)
        {
            logger.LogWarning(
                ex,
                "Notification adapter {AdapterType} failed for message type {MessageType}. Error is suppressed.",
                adapter.GetType().Name,
                typeof(TMessage).Name);
        }
    }
}
