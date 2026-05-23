using Microsoft.Extensions.DependencyInjection;
using NetMediate.Resilience.DependencyInjection;

namespace NetMediate.Resilience;

public static class ServiceCollectionsExtensions
{
    public static void AddResilience(this IServiceCollection services)
    {
        services.AddGenDIServices();
    }
}
