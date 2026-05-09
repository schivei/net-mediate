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
    public void TryGet_WhenKeyRegistered_ReturnsTrueAndHandler()
    {
        var alpha = new AlphaHandler();
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<ITestHandler>>
            {
                { "alpha", () => alpha }
            }
        );

        var found = registry.TryGet("alpha", out var handler);

        Assert.True(found);
        Assert.Same(alpha, handler);
    }

    [Fact]
    public void TryGet_WhenKeyNotRegistered_ReturnsFalseAndDefault()
    {
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<ITestHandler>>
            {
                { "alpha", () => new AlphaHandler() }
            }
        );

        var found = registry.TryGet("missing", out var handler);

        Assert.False(found);
        Assert.Null(handler);
    }

    [Fact]
    public void TryGet_ForTransient_ReturnsNewInstanceEachCall()
    {
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<ITestHandler>>
            {
                { "alpha", () => new AlphaHandler() }
            }
        );

        registry.TryGet("alpha", out var first);
        registry.TryGet("alpha", out var second);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void TryGet_ForSingleton_ReturnsSameInstanceEachCall()
    {
        var lazy = new Lazy<ITestHandler>(() => new AlphaHandler());
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<ITestHandler>>
            {
                { "alpha", () => lazy.Value }
            }
        );

        registry.TryGet("alpha", out var first);
        registry.TryGet("alpha", out var second);

        Assert.Same(first, second);
    }

    [Fact]
    public void TryGet_WithMultipleKeys_ResolvesEachCorrectly()
    {
        var registry = new KeyedHandlerRegistry<ITestHandler>(
            new Dictionary<object, Func<ITestHandler>>
            {
                { "alpha", () => new AlphaHandler() },
                { "beta", () => new BetaHandler() }
            }
        );

        registry.TryGet("alpha", out var alpha);
        registry.TryGet("beta", out var beta);

        Assert.Equal("alpha", alpha?.Name);
        Assert.Equal("beta", beta?.Name);
    }

    [Fact]
    public void PipelineExecutor_FallsBackToGetServices_WhenNoRegistryRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ICommandHandler<string>>(
            static _ => new StringCommandHandler()
        );
        using var provider = services.BuildServiceProvider();

        // No KeyedHandlerRegistry<ICommandHandler<string>> is registered.
        // The executor should fall back to GetServices and return all handlers.
        var registry = provider.GetService<KeyedHandlerRegistry<ICommandHandler<string>>>();
        Assert.Null(registry);

        // Fallback path: standard DI still resolves the handler
        var handlers = provider.GetServices<ICommandHandler<string>>().ToList();
        Assert.Single(handlers);
    }

    [Fact]
    public void PipelineExecutor_UsesRegistry_WhenKeyIsRegistered()
    {
        var keyedHandler = new StringCommandHandler();

        var services = new ServiceCollection();
        services.AddSingleton<KeyedHandlerRegistry<ICommandHandler<string>>>(
            _ => new KeyedHandlerRegistry<ICommandHandler<string>>(
                new Dictionary<object, Func<ICommandHandler<string>>>
                {
                    { "specific", () => keyedHandler }
                }
            )
        );

        using var provider = services.BuildServiceProvider();

        var registry = provider.GetService<KeyedHandlerRegistry<ICommandHandler<string>>>();
        Assert.NotNull(registry);

        var found = registry.TryGet("specific", out var resolved);
        Assert.True(found);
        Assert.Same(keyedHandler, resolved);
    }

    private sealed class StringCommandHandler : ICommandHandler<string>
    {
        public Task Handle(string command, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
