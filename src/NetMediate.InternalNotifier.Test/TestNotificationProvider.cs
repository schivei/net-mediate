using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.InternalNotifier.Test;

/// <summary>
/// Test notification provider that dispatches handlers directly on the calling thread,
/// completing the entire notification pipeline before the awaited
/// <see cref="IMediator.Notify{TMessage}"/> call returns.
/// </summary>
/// <remarks>
/// <para>
/// Use this provider in unit and integration tests to avoid <c>Task.Delay</c> or
/// <c>Thread.Sleep</c> waits after a notification is published.  Because dispatch is
/// synchronous the test can immediately assert on side-effects produced by handlers.
/// </para>
/// <para>
/// Register via
/// <see cref="TestNotifierExtensions.AddNetMediateTestNotifier"/>.
/// </para>
/// </remarks>
public sealed class TestNotificationProvider(INotificationDispatcher dispatcher) : INotificationProvider
{
    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ValueTask EnqueueAsync<TMessage>(
        TMessage message,
        CancellationToken cancellationToken) =>
        new(dispatcher.DispatchAsync(message, cancellationToken));
}
