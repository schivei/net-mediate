using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMediate.Internals.Workers;

namespace NetMediate.Internals;

internal sealed class MediatorServiceBuilder<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TNotifier>
    : IMediatorServiceBuilder where TNotifier : class, INotifiable
{
    public static ConfigurationOptions ConfigureOptions { get; private set; } = null!;

    public IServiceCollection Services { get; }

    internal MediatorServiceBuilder(IServiceCollection services)
    {
        Services = services;

        ConfigureOptions = new ConfigurationOptions(Channel.CreateUnbounded<IPack>());
        Services.ConfigureOptions(ConfigureOptions);
        Services.TryAddSingleton<IMediator, Mediator>();
        Services.TryAddSingleton<Configuration>();

        if (Services.Any(s => s.ServiceType == typeof(INotifiable)))
        {
            Services.Replace(ServiceDescriptor.Singleton<INotifiable, TNotifier>());
        }
        else
        {
            Services.AddSingleton<INotifiable, TNotifier>();
        }

        if (typeof(TNotifier) != typeof(Notifier))
            return;
        
        Services.TryAddSingleton<NotificationWorker>();
        Services.AddHostedService(sp => sp.GetRequiredService<NotificationWorker>());
    }

    public IMediatorServiceBuilder IgnoreUnhandledMessages(
        bool ignore = true
    )
    {
        ConfigureOptions.IgnoreUnhandledMessages = ignore;

        return this;
    }
    
    public IMediatorServiceBuilder RegisterHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler, TMessage, TResult>(
        Func<IServiceProvider, object?, THandler>? handler = null
    ) where THandler : class, IHandler<TMessage, TResult>, new()
    {
        if (handler == null)
        {
            Services.AddKeyedSingleton<THandler>(typeof(TMessage));
        }
        else
        {
            Services.AddKeyedSingleton<THandler>(typeof(TMessage), handler);
        }

        return this;
    }
}
