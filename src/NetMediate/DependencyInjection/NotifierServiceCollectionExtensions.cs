using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NetMediate.Internals;

namespace NetMediate.DependencyInjection;

/// <summary>
/// Service-registration helpers for the default in-process notifier.
/// </summary>
public static class NotifierServiceCollectionExtensions
{
    /// <summary>
    /// Registers the default <see cref="INotifiable"/> implementation only when no custom registration exists.
    /// </summary>
    public static IServiceCollection TryAddDefaultNetMediateNotifier(this IServiceCollection services)
    {
        services.TryAddSingleton<INotifiable, Notifier>();
        return services;
    }
}

