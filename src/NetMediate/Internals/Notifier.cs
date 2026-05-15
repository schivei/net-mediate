using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

[Injectable]
internal class Notifier(IServiceProvider serviceProvider) : INotifiable
{
    // Handler cache — populated once per notification type, on first dispatch.
    private readonly ConcurrentDictionary<Type, object> _cache = new();

    private INotificationHandler<TMessage>[] GetHandlers<TMessage>()
        where TMessage : notnull =>
        (INotificationHandler<TMessage>[])_cache.GetOrAdd(
            typeof(TMessage),
            _ => (object)serviceProvider.GetServices<INotificationHandler<TMessage>>().ToArray());

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

        // Fire each handler individually (true fire-and-forget). Async handlers whose tasks are
        // not yet completed are observed via ContinueWith to prevent UnobservedTaskException.
        // Exceptions are intentionally swallowed here; the caller is responsible for fault handling
        // when using DispatchNotifications directly. The pipeline path (via Handle) logs faults.
        foreach (var h in handlers)
        {
            var t = h.Handle(message, cancellationToken);
            if (!t.IsCompletedSuccessfully)
                _ = t.ContinueWith(static _ => { }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }

        return Task.CompletedTask;
    }

    public Task Notify<TMessage>(
        object? key,
        TMessage message,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        INotificationHandler<TMessage>[] handlers = key is null
            ? GetHandlers<TMessage>()
            : [.. serviceProvider.GetKeyedServices<INotificationHandler<TMessage>>(key)];

        // Fire-and-forget: discard the Task returned by the pipeline. ErrorReporting inside the
        // executor logs any handler exceptions and ensures the Task is never faulted, so the
        // discard here is safe.
        _ = DispatchNotifications(key, message, handlers, cancellationToken);

        return Task.CompletedTask;
    }

    public Task Notify<TMessage>(
        object? key,
        IEnumerable<TMessage> messages,
        CancellationToken cancellationToken = default
    )
        where TMessage : notnull
    {
        // Each single-message Notify returns Task.CompletedTask immediately, so iterating
        // synchronously is equivalent to Task.WhenAll with zero async overhead.
        foreach (var m in messages)
            Notify(key, m, cancellationToken);

        return Task.CompletedTask;
    }
}
