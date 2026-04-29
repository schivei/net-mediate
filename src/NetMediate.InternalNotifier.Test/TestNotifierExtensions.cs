using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.InternalNotifier.Test;

/// <summary>
/// Extension methods for registering the test-friendly synchronous notification provider.
/// </summary>
public static class TestNotifierExtensions
{
    /// <summary>
    /// Replaces the default notification provider with <see cref="TestNotificationProvider"/>,
    /// which dispatches handlers inline on the calling thread.
    /// </summary>
    /// <param name="builder">The mediator service builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// After this call every <see cref="IMediator.Notify{TMessage}"/> awaited in a test will
    /// complete only after all registered <see cref="INotificationHandler{TMessage}"/>
    /// instances have finished, making post-notification assertions safe without any
    /// artificial delays.
    /// </remarks>
    public static IMediatorServiceBuilder AddNetMediateTestNotifier(
        this IMediatorServiceBuilder builder)
    {
        builder.Services.Replace(
            ServiceDescriptor.Singleton<INotificationProvider, TestNotificationProvider>());

        return builder;
    }
}
