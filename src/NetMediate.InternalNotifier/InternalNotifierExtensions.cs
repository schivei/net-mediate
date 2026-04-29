using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NetMediate.Internals;

namespace NetMediate.InternalNotifier;

/// <summary>
/// Extension methods for registering the Channel-based background notification worker.
/// </summary>
public static class InternalNotifierExtensions
{
    /// <summary>
    /// Registers the Channel-based <see cref="BackgroundNotificationWorker"/> as a hosted
    /// service and replaces the default inline <see cref="INotificationProvider"/> with a
    /// channel-writing provider, enabling true fire-and-forget notification delivery.
    /// </summary>
    /// <param name="builder">The mediator service builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <remarks>
    /// Notifications enqueued via <see cref="IMediator.Notify{TMessage}"/> are written to an
    /// in-process <see cref="Channel{T}"/> and dispatched asynchronously by the background
    /// worker.  Exceptions thrown by handlers are logged at <c>Trace</c> level and do not
    /// propagate to the caller.
    /// </remarks>
    public static IMediatorServiceBuilder AddNetMediateInternalNotifier(
        this IMediatorServiceBuilder builder)
    {
        var channel = Channel.CreateUnbounded<INotificationPacket>(
            new UnboundedChannelOptions { SingleReader = true });

        builder.Services.TryAddSingleton(channel.Reader);
        builder.Services.TryAddSingleton(channel.Writer);
        builder.Services.Replace(
            ServiceDescriptor.Singleton<INotificationProvider>(
                sp => new ChannelNotificationProvider(
                    sp.GetRequiredService<ChannelWriter<INotificationPacket>>())));
        builder.Services.TryAddSingleton<BackgroundNotificationWorker>();
        builder.Services.AddHostedService(
            sp => sp.GetRequiredService<BackgroundNotificationWorker>());

        return builder;
    }
}
