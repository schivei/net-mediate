using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NetMediate.Moq;

/// <summary>
/// Factory helpers for fast and expressive mock creation.
/// </summary>
public static class Mocking
{
    /// <summary>
    /// Creates a new <see cref="Moq.Mock{T}"/> with optional behavior.
    /// </summary>
    /// <typeparam name="T">Service type being mocked.</typeparam>
    /// <param name="behavior">Optional mock behavior.</param>
    /// <returns>Configured mock instance.</returns>
    public static global::Moq.Mock<T> Create<T>(
        global::Moq.MockBehavior behavior = global::Moq.MockBehavior.Default
    )
        where T : class => new(behavior);

    /// <summary>
    /// Creates a strict mock.
    /// </summary>
    /// <typeparam name="T">Service type being mocked.</typeparam>
    /// <returns>Strict mock instance.</returns>
    public static global::Moq.Mock<T> Strict<T>()
        where T : class => new(global::Moq.MockBehavior.Strict);

    /// <summary>
    /// Creates a loose mock.
    /// </summary>
    /// <typeparam name="T">Service type being mocked.</typeparam>
    /// <returns>Loose mock instance.</returns>
    public static global::Moq.Mock<T> Loose<T>()
        where T : class => new(global::Moq.MockBehavior.Loose);

    /// <summary>
    /// Creates and registers a singleton mock object in the service collection.
    /// Existing registrations for the same service are removed.
    /// </summary>
    /// <typeparam name="TService">Service type to register.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="behavior">Optional mock behavior.</param>
    /// <returns>The created mock instance.</returns>
    public static global::Moq.Mock<TService> AddMockSingleton<TService>(
        this IServiceCollection services,
        global::Moq.MockBehavior behavior = global::Moq.MockBehavior.Default
    )
        where TService : class
    {
        Guard.ThrowIfNull(services);

        var mock = new global::Moq.Mock<TService>(behavior);
        services.ReplaceWithMock(mock);
        return mock;
    }

    /// <summary>
    /// Registers an existing mock object as singleton in the service collection.
    /// Existing registrations for the same service are removed.
    /// </summary>
    /// <typeparam name="TService">Service type to register.</typeparam>
    /// <param name="services">Service collection.</param>
    /// <param name="mock">Existing mock.</param>
    /// <returns>The provided mock instance.</returns>
    public static global::Moq.Mock<TService> AddMockSingleton<TService>(
        this IServiceCollection services,
        global::Moq.Mock<TService> mock
    )
        where TService : class
    {
        Guard.ThrowIfNull(services);
        Guard.ThrowIfNull(mock);

        services.ReplaceWithMock(mock);
        return mock;
    }

    /// <summary>
    /// Creates and registers a singleton mock for the NetMediate mediator.
    /// </summary>
    /// <param name="services">Service collection.</param>
    /// <param name="behavior">Optional mock behavior.</param>
    /// <returns>The created mediator mock instance.</returns>
    public static global::Moq.Mock<IMediator> AddMediatorMock(
        this IServiceCollection services,
        global::Moq.MockBehavior behavior = global::Moq.MockBehavior.Default
    ) => services.AddMockSingleton<IMediator>(behavior);

    private static void ReplaceWithMock<TService>(
        this IServiceCollection services,
        global::Moq.Mock<TService> mock
    )
        where TService : class
    {
        services.RemoveAll<TService>();
        services.RemoveAll<global::Moq.Mock<TService>>();
        services.AddSingleton(mock);
        services.AddSingleton(_ => mock.Object);
    }
}
