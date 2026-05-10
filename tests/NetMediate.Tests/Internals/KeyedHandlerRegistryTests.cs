using Microsoft.Extensions.DependencyInjection;

namespace NetMediate.Tests.Internals;

/// <summary>
/// Tests the AOT-safe <see cref="KeyedHandlerRegistry{THandler}"/> used by pipeline executors
/// to resolve handlers by routing key without reflection-based Microsoft DI keyed APIs.
/// </summary>
public sealed class KeyedHandlerRegistryTests
{
    private interface ITestHandler
    {
        string Name { get; }
    }

    private sealed class AlphaHandler : ITestHandler { public string Name => "alpha"; }
    private sealed class BetaHandler : ITestHandler { public string Name => "beta"; }

    [Fact]
    public void TryGetAll_WhenKeyRegistered_ReturnsTrueAndHandler()
    {
        var alpha = new AlphaHandler();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<IServiceProvider, ITestHandler>[]>
            {
                { "alpha", [_ => alpha] }
            }
        );

        var found = registry.TryGetAll("alpha", provider, out var handlers);

        Assert.True(found);
        Assert.Single(handlers);
        Assert.Same(alpha, handlers[0]);
    }

    [Fact]
    public void TryGetAll_WhenKeyNotRegistered_ReturnsFalseAndEmptyArray()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<IServiceProvider, ITestHandler>[]>
            {
                { "alpha", [_ => new AlphaHandler()] }
            }
        );

        var found = registry.TryGetAll("missing", provider, out var handlers);

        Assert.False(found);
        Assert.Empty(handlers);
    }

    [Fact]
    public void TryGetAll_ForTransient_ReturnsNewInstanceEachCall()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<IServiceProvider, ITestHandler>[]>
            {
                { "alpha", [_ => new AlphaHandler()] }
            }
        );

        registry.TryGetAll("alpha", provider, out var first);
        registry.TryGetAll("alpha", provider, out var second);

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);
        Assert.NotSame(first[0], second[0]);
    }

    [Fact]
    public void TryGetAll_ForSingleton_ReturnsSameInstanceEachCall()
    {
        var lazy = new Lazy<ITestHandler>(() => new AlphaHandler());
        using var provider = new ServiceCollection().BuildServiceProvider();
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<IServiceProvider, ITestHandler>[]>
            {
                { "alpha", [_ => lazy.Value] }
            }
        );

        registry.TryGetAll("alpha", provider, out var first);
        registry.TryGetAll("alpha", provider, out var second);

        Assert.Same(first[0], second[0]);
    }

    [Fact]
    public void TryGetAll_WithMultipleKeys_ResolvesEachCorrectly()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<IServiceProvider, ITestHandler>[]>
            {
                { "alpha", [_ => new AlphaHandler()] },
                { "beta", [_ => new BetaHandler()] }
            }
        );

        registry.TryGetAll("alpha", provider, out var alphaHandlers);
        registry.TryGetAll("beta", provider, out var betaHandlers);

        Assert.Equal("alpha", alphaHandlers[0].Name);
        Assert.Equal("beta", betaHandlers[0].Name);
    }

    [Fact]
    public void TryGetAll_WithMultipleHandlersSameKey_ReturnsAllHandlers()
    {
        var alpha = new AlphaHandler();
        var beta = new BetaHandler();
        using var provider = new ServiceCollection().BuildServiceProvider();
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<IServiceProvider, ITestHandler>[]>
            {
                { "multi", [_ => alpha, _ => beta] }
            }
        );

        var found = registry.TryGetAll("multi", provider, out var handlers);

        Assert.True(found);
        Assert.Equal(2, handlers.Length);
        Assert.Same(alpha, handlers[0]);
        Assert.Same(beta, handlers[1]);
    }

    [Fact]
    public void TryGetAll_ForScoped_UsesCurrentScopeServiceProvider()
    {
        // Scoped factory receives the active IServiceProvider.
        // Verify that the factory is called with the provider passed to TryGetAll.
        IServiceProvider? capturedProvider = null;
        using var rootProvider = new ServiceCollection().BuildServiceProvider();

        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<IServiceProvider, ITestHandler>[]>
            {
                {
                    "scoped",
                    [sp =>
                    {
                        capturedProvider = sp;
                        return new AlphaHandler();
                    }]
                }
            }
        );

        registry.TryGetAll("scoped", rootProvider, out _);

        Assert.Same(rootProvider, capturedProvider);
    }

    [Fact]
    public void Registry_WhenNotRegistered_FallbackToGetServices_ReturnsAllHandlers()
    {
        // When no KeyedHandlerRegistry is registered in DI, GetService returns null.
        // Pipeline executors fall back to GetServices<T>() in this case.
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<string>>(
            static _ => new StringCommandHandler()
        );
        using var provider = services.BuildServiceProvider();

        var registry = provider.GetService<KeyedHandlerRegistry<ICommandHandler<string>>>();
        Assert.Null(registry);

        // Fallback path: standard DI resolves the handler
        var handlers = provider.GetServices<ICommandHandler<string>>().ToList();
        Assert.Single(handlers);
    }

    [Fact]
    public void Registry_WhenRegistered_TryGetAllResolvesCorrectHandlers()
    {
        var keyedHandler = new StringCommandHandler();
        using var provider = new ServiceCollection().BuildServiceProvider();

        var registry = new KeyedHandlerRegistry<ICommandHandler<string>>(
            new Dictionary<object, Func<IServiceProvider, ICommandHandler<string>>[]>
            {
                { "specific", [_ => keyedHandler] }
            }
        );

        var found = registry.TryGetAll("specific", provider, out var handlers);
        Assert.True(found);
        Assert.Single(handlers);
        Assert.Same(keyedHandler, handlers[0]);
    }

    private sealed class StringCommandHandler : ICommandHandler<string>
    {
        public Task Handle(string command, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
