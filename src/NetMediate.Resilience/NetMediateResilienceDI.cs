using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Resilience;

/// <summary>
/// Dependency injection extensions for NetMediate resilience behaviors.
/// </summary>
public static class NetMediateResilienceDI
{
    /// <summary>
    /// Registers retry, timeout and circuit-breaker behaviors for request and notification pipelines.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureRetry">Optional retry options configuration.</param>
    /// <param name="configureTimeout">Optional timeout options configuration.</param>
    /// <param name="configureCircuitBreaker">Optional circuit-breaker options configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddNetMediateResilience(
        this IServiceCollection services,
        Action<RetryBehaviorOptions>? configureRetry = null,
        Action<TimeoutBehaviorOptions>? configureTimeout = null,
        Action<CircuitBreakerBehaviorOptions>? configureCircuitBreaker = null
    )
    {
        services.AddNetMediateRetry(configureRetry);
        services.AddNetMediateTimeout(configureTimeout);
        services.AddNetMediateCircuitBreaker(configureCircuitBreaker);
        return services;
    }

    /// <summary>
    /// Registers retry behaviors for request and notification pipelines.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional retry options configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddNetMediateRetry(
        this IServiceCollection services,
        Action<RetryBehaviorOptions>? configure = null
    )
    {
        var options = new RetryBehaviorOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped(typeof(IRequestBehavior<,>), typeof(RetryRequestBehavior<,>));
        services.AddScoped(typeof(INotificationBehavior<>), typeof(RetryNotificationBehavior<>));
        return services;
    }

    /// <summary>
    /// Registers timeout behaviors for request and notification pipelines.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional timeout options configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddNetMediateTimeout(
        this IServiceCollection services,
        Action<TimeoutBehaviorOptions>? configure = null
    )
    {
        var options = new TimeoutBehaviorOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped(typeof(IRequestBehavior<,>), typeof(TimeoutRequestBehavior<,>));
        services.AddScoped(typeof(INotificationBehavior<>), typeof(TimeoutNotificationBehavior<>));
        return services;
    }

    /// <summary>
    /// Registers circuit-breaker behaviors for request and notification pipelines.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional circuit-breaker options configuration.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddNetMediateCircuitBreaker(
        this IServiceCollection services,
        Action<CircuitBreakerBehaviorOptions>? configure = null
    )
    {
        var options = new CircuitBreakerBehaviorOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped(
            typeof(IRequestBehavior<,>),
            typeof(CircuitBreakerRequestBehavior<,>)
        );
        services.AddScoped(
            typeof(INotificationBehavior<>),
            typeof(CircuitBreakerNotificationBehavior<>)
        );
        return services;
    }
}
