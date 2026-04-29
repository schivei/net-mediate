using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Internals;

/// <summary>
/// Default <see cref="INotificationProvider"/> registered when no custom provider is added.
/// Dispatches notification handlers directly on the calling thread without any
/// background queue or worker.
/// </summary>
/// <remarks>
/// <para>
/// The dependency on <see cref="INotificationDispatcher"/> is resolved lazily via
/// <see cref="IServiceProvider"/> to break the circular dependency chain at DI
/// construction time.
/// </para>
/// <para>
/// For true fire-and-forget delivery via a background worker, add the
/// <c>NetMediate.InternalNotifier</c> package and call
/// <c>AddNetMediateInternalNotifier()</c> on the builder.
/// </para>
/// </remarks>
internal sealed class BuiltInNotificationProvider(IServiceProvider serviceProvider) : INotificationProvider
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken) =>
        new(serviceProvider
            .GetRequiredService<INotificationDispatcher>()
            .DispatchAsync(message, cancellationToken));
}
