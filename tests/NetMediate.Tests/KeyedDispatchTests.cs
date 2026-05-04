using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests;

public sealed class KeyedDispatchTests
{
    [Fact]
    public async Task KeyedSend_ShouldDispatchOnlyToHandlerWithMatchingKey()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterKeyedCommandHandler<KeyedCommandHandlerA, KeyedCommand>("a");
            reg.RegisterKeyedCommandHandler<KeyedCommandHandlerB, KeyedCommand>("b");
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var cmd = new KeyedCommand();
        await mediator.Send("a", cmd, ct);

        Assert.True(cmd.RanA);
        Assert.False(cmd.RanB);
    }

    [Fact]
    public async Task KeyedSend_WithUnknownKey_ShouldBeNoOp()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterKeyedCommandHandler<KeyedCommandHandlerA, KeyedCommand>("a");
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        // Dispatching with an unknown key should not throw.
        await mediator.Send("z", new KeyedCommand(), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task KeyedNotify_ShouldDispatchOnlyToHandlerWithMatchingKey()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterKeyedNotificationHandler<KeyedNotificationHandlerA, KeyedNotification>("a");
            reg.RegisterKeyedNotificationHandler<KeyedNotificationHandlerB, KeyedNotification>("b");
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var msg = new KeyedNotification();
        await mediator.Notify("b", msg, ct);

        Assert.False(msg.RanA);
        Assert.True(msg.RanB);
    }

    [Fact]
    public async Task KeyedRequest_ShouldDispatchToHandlerWithMatchingKey()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterKeyedRequestHandler<KeyedRequestHandlerA, KeyedRequest, string>("a");
            reg.RegisterKeyedRequestHandler<KeyedRequestHandlerB, KeyedRequest, string>("b");
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var responseA = await mediator.Request<KeyedRequest, string>("a", new KeyedRequest(), ct);
        var responseB = await mediator.Request<KeyedRequest, string>("b", new KeyedRequest(), ct);

        Assert.Equal("handler-a", responseA);
        Assert.Equal("handler-b", responseB);
    }

    [Fact]
    public async Task KeyedRequestStream_ShouldMergeAllHandlersWithMatchingKey()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterKeyedStreamHandler<KeyedStreamHandlerA, KeyedStreamMessage, int>("x");
            reg.RegisterKeyedStreamHandler<KeyedStreamHandlerB, KeyedStreamMessage, int>("x");
            reg.RegisterKeyedStreamHandler<KeyedStreamHandlerC, KeyedStreamMessage, int>("y");
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        var ct = TestContext.Current.CancellationToken;

        var results = await mediator
            .RequestStream<KeyedStreamMessage, int>("x", new KeyedStreamMessage(), ct)
            .AsyncToSync();

        // HandlerA (key "x") yields 1,2 and HandlerB (key "x") yields 3,4; HandlerC ("y") not invoked.
        Assert.Equal([1, 2, 3, 4], [.. results]);
    }

    // ── Message types ──

    private sealed class KeyedCommand
    {
        public bool RanA { get; set; }
        public bool RanB { get; set; }
    }

    private sealed class KeyedNotification
    {
        public bool RanA { get; set; }
        public bool RanB { get; set; }
    }

    private sealed record KeyedRequest;

    private sealed record KeyedStreamMessage;

    // ── Handlers ──

    private sealed class KeyedCommandHandlerA : ICommandHandler<KeyedCommand>
    {
        public Task Handle(KeyedCommand command, CancellationToken cancellationToken = default)
        {
            command.RanA = true;
            return Task.CompletedTask;
        }
    }

    private sealed class KeyedCommandHandlerB : ICommandHandler<KeyedCommand>
    {
        public Task Handle(KeyedCommand command, CancellationToken cancellationToken = default)
        {
            command.RanB = true;
            return Task.CompletedTask;
        }
    }

    private sealed class KeyedNotificationHandlerA : INotificationHandler<KeyedNotification>
    {
        public Task Handle(KeyedNotification notification, CancellationToken cancellationToken = default)
        {
            notification.RanA = true;
            return Task.CompletedTask;
        }
    }

    private sealed class KeyedNotificationHandlerB : INotificationHandler<KeyedNotification>
    {
        public Task Handle(KeyedNotification notification, CancellationToken cancellationToken = default)
        {
            notification.RanB = true;
            return Task.CompletedTask;
        }
    }

    private sealed class KeyedRequestHandlerA : IRequestHandler<KeyedRequest, string>
    {
        public Task<string> Handle(KeyedRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult("handler-a");
    }

    private sealed class KeyedRequestHandlerB : IRequestHandler<KeyedRequest, string>
    {
        public Task<string> Handle(KeyedRequest query, CancellationToken cancellationToken = default) =>
            Task.FromResult("handler-b");
    }

    private sealed class KeyedStreamHandlerA : IStreamHandler<KeyedStreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            KeyedStreamMessage message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return 1;
            yield return 2;
            await Task.CompletedTask;
        }
    }

    private sealed class KeyedStreamHandlerB : IStreamHandler<KeyedStreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            KeyedStreamMessage message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return 3;
            yield return 4;
            await Task.CompletedTask;
        }
    }

    private sealed class KeyedStreamHandlerC : IStreamHandler<KeyedStreamMessage, int>
    {
        public async IAsyncEnumerable<int> Handle(
            KeyedStreamMessage message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            yield return 99;
            await Task.CompletedTask;
        }
    }
}
