using Microsoft.Extensions.DependencyInjection;
using NetMediate.DependencyInjection;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace NetMediate;

/// <summary>
/// Extension methods for IServiceCollection to register NetMediate services.
/// </summary>
/// <remarks>Call RegisterNetMediate on an IServiceCollection to add NetMediate's required dependencies; it
/// delegates to AddGenDIServices.</remarks>
[ExcludeFromCodeCoverage]
public static class NetMediateExtensions
{
    private static bool s_isRegistered;
    private static readonly Lock s_lock = new();

    /// <summary>
    /// Registers NetMediate services into the dependency injection container.
    /// </summary>
    /// <remarks>Delegates the registrations to AddGenDIServices.</remarks>
    /// <param name="services">The service collection to which NetMediate services are added.</param>
    [Browsable(false)]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static void RegisterNetMediate(this IServiceCollection services)
    {
        lock (s_lock)
        {
            if (s_isRegistered)
                return;

            s_isRegistered = true;
        }

        services.AddGenDIServices();
    }
}
