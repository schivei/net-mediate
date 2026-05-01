using Microsoft.Extensions.DependencyInjection;

namespace NetMediate;

/// <summary>
/// Fluent builder for configuring mediator services: registering handlers, attaching per-handler predicates (filters),
/// customizing handler instantiation, and setting behavior for unhandled messages.
/// </summary>
public interface IMediatorServiceBuilder
{
    /// <summary>
    /// Gets the DI service collection being configured.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Configures how unhandled messages are treated.
    /// </summary>
    /// <param name="ignore">If true, absence of a handler does not throw.</param>
    /// <returns>This builder for chaining.</returns>
    IMediatorServiceBuilder IgnoreUnhandledMessages(bool ignore = true);
}
