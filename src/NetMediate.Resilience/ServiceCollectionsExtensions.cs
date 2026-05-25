using Microsoft.Extensions.DependencyInjection;
using NetMediate.Resilience.DependencyInjection;
using System.Diagnostics.CodeAnalysis;

namespace NetMediate.Resilience;

[ExcludeFromCodeCoverage]
public static class ServiceCollectionsExtensions
{
    public static void AddResilience(this IServiceCollection services)
    {
        services.AddGenDIServices();
    }
}
