using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate.Internals;

namespace NetMediate.Tests;

public sealed class PipelineBehaviorTests
{
    public PipelineBehaviorTests()
    {
        // Clear the global handler cache to prevent cross-test contamination.
        // Each test creates its own DI container with different handler instances;
        // the static cache would otherwise return stale instances from a prior test.
        Extensions.ClearCache();
    }

    [Fact]
    public async Task RequestBehavior_ShouldRunInOrderAndWrapResponse()
    {
        using var host = await CreateHostAsync(services =>
        {
            services.AddScoped<
                IPipelineBehavior<PipelineRequest, Task<string>>,
                FirstRequestBehavior
            >();
            services.AddScoped<
                IPipelineBehavior<PipelineRequest, Task<string>>,
                SecondRequestBehavior
            >();
            services.AddSingleton<CallTrace>();
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        var trace = host.Services.GetRequiredService<CallTrace>();
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await mediator.Request<PipelineRequest, string>(
            new("ok"),
            cancellationToken
        );

        Assert.Equal("ok:second:first", response);
        Assert.Equal(
            [
                "request:first:pre",
                "request:second:pre",
                "request:handler",
                "request:second:post",
                "request:first:post",
            ],
            trace.ToArray()
        );
    }

    [Fact]
    public async Task CommandBehavior_ShouldRunBeforeAndAfterHandler()
    {
        using var host = await CreateHostAsync(services =>
        {
            services.AddScoped<IPipelineBehavior<PipelineCommand, Task>, CommandBehavior>();
            services.AddSingleton<CallTrace>();
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        var trace = host.Services.GetRequiredService<CallTrace>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await mediator.Send(new PipelineCommand("ok"), cancellationToken);

        Assert.Equal(["command:pre", "command:handler", "command:post"], trace.ToArray());
    }

    [Fact]
    public async Task StreamBehavior_ShouldWrapFullStreamExecution()
    {
        using var host = await CreateHostAsync(services =>
        {
            services.AddScoped<
                IPipelineBehavior<PipelineStream, IAsyncEnumerable<int>>,
                StreamBehavior
            >();
            services.AddSingleton<CallTrace>();
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        var trace = host.Services.GetRequiredService<CallTrace>();
        var cancellationToken = TestContext.Current.CancellationToken;

        var values = await mediator
            .RequestStream<PipelineStream, int>(new(3), cancellationToken)
            .AsyncToSync();

        Assert.Equal([1, 2, 3], values);
        Assert.Equal(
            ["stream:pre", "stream:handler:start", "stream:handler:end", "stream:post"],
            trace.ToArray()
        );
    }

    [Fact]
    public async Task NotificationBehavior_ShouldWrapNotificationDispatch()
    {
        using var host = await CreateHostAsync(services =>
        {
            services.AddScoped<
                IPipelineBehavior<PipelineNotification, Task>,
                NotificationBehavior
            >();
            services.AddSingleton<CallTrace>();
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        var trace = host.Services.GetRequiredService<CallTrace>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await mediator.Notify(new PipelineNotification("ok"), cancellationToken);
        await WaitForTraceSizeAsync(trace, expected: 5, cancellationToken);

        var entries = trace.ToArray();
        Assert.Equal(5, entries.Length);
        Assert.Equal("notification:pre", entries[0]);
        Assert.Contains("notification:handler:1", entries);
        Assert.Contains("notification:handler:2", entries);
        Assert.Equal("notification:post", entries[^2]);
        Assert.Equal("notification:after-await", entries[^1]);
    }

    [Fact]
    public async Task NotificationBehaviorShorthand_ShouldWrapNotificationDispatch()
    {
        using var host = await CreateHostAsync(services =>
        {
            services.AddScoped<
                IPipelineNotificationBehavior<PipelineNotification>,
                ShorthandNotificationBehavior
            >();
            services.AddSingleton<CallTrace>();
        });

        var mediator = host.Services.GetRequiredService<IMediator>();
        var trace = host.Services.GetRequiredService<CallTrace>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await mediator.Notify(new PipelineNotification("ok"), cancellationToken);
        await WaitForTraceSizeAsync(trace, expected: 5, cancellationToken);

        var entries = trace.ToArray();
        Assert.Equal(5, entries.Length);
        Assert.Equal("notification:pre", entries[0]);
        Assert.Contains("notification:handler:1", entries);
        Assert.Contains("notification:handler:2", entries);
        Assert.Equal("notification:post", entries[^2]);
        Assert.Equal("notification:after-await", entries[^1]);
    }

    private static async Task WaitForTraceSizeAsync(
        CallTrace trace,
        int expected,
        CancellationToken cancellationToken
    )
    {
        const int timeoutMilliseconds = 2000;
        const int delayMilliseconds = 10;
        const int maxAttempts = timeoutMilliseconds / delayMilliseconds;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (trace.Count >= expected)
                return;

            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(delayMilliseconds, cancellationToken);
        }

        Assert.Fail($"Timed out waiting for trace size {expected}. Current size: {trace.Count}.");
    }

    private static async Task<IHost> CreateHostAsync(Action<IServiceCollection> setup)
    {
        var builder = Host.CreateApplicationBuilder();
        setup(builder.Services);
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterRequestHandler<PipelineRequestHandler, PipelineRequest, string>();
            reg.RegisterCommandHandler<PipelineCommandHandler, PipelineCommand>();
            reg.RegisterStreamHandler<PipelineStreamHandler, PipelineStream, int>();
            reg.RegisterNotificationHandler<PipelineNotificationHandler1, PipelineNotification>();
            reg.RegisterNotificationHandler<PipelineNotificationHandler2, PipelineNotification>();
        });

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    public sealed record PipelineRequest(string Value);

    public sealed record PipelineCommand(string Value);

    public sealed record PipelineNotification(string Value);

    public sealed record PipelineStream(int Count);

    private sealed class CallTrace
    {
        private readonly ConcurrentQueue<string> _calls = new();
        public int Count => _calls.Count;

        public void Add(string value) => _calls.Enqueue(value);

        public string[] ToArray() => [.. _calls];
    }

    private sealed class PipelineRequestHandler(CallTrace trace)
        : IRequestHandler<PipelineRequest, string>
    {
        public Task<string> Handle(
            PipelineRequest query,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("request:handler");
            return Task.FromResult(query.Value);
        }
    }

    private sealed class FirstRequestBehavior(CallTrace trace)
        : IPipelineRequestBehavior<PipelineRequest, string>
    {
        public async Task<string> Handle(
            object? key,
            PipelineRequest message,
            PipelineBehaviorDelegate<PipelineRequest, Task<string>> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("request:first:pre");
            var response = await next(key, message, cancellationToken);
            trace.Add("request:first:post");
            return $"{response}:first";
        }
    }

    private sealed class SecondRequestBehavior(CallTrace trace)
        : IPipelineRequestBehavior<PipelineRequest, string>
    {
        public async Task<string> Handle(
            object? key,
            PipelineRequest message,
            PipelineBehaviorDelegate<PipelineRequest, Task<string>> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("request:second:pre");
            var response = await next(key, message, cancellationToken);
            trace.Add("request:second:post");
            return $"{response}:second";
        }
    }

    private sealed class PipelineCommandHandler(CallTrace trace) : ICommandHandler<PipelineCommand>
    {
        public Task Handle(PipelineCommand command, CancellationToken cancellationToken = default)
        {
            trace.Add("command:handler");
            return Task.CompletedTask;
        }
    }

    private sealed class CommandBehavior(CallTrace trace) : IPipelineBehavior<PipelineCommand, Task>
    {
        public async Task Handle(
            object? key,
            PipelineCommand message,
            PipelineBehaviorDelegate<PipelineCommand, Task> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("command:pre");
            await next(key, message, cancellationToken);
            trace.Add("command:post");
        }
    }

    private sealed class PipelineStreamHandler(CallTrace trace)
        : IStreamHandler<PipelineStream, int>
    {
        public async IAsyncEnumerable<int> Handle(
            PipelineStream query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            trace.Add("stream:handler:start");
            for (var i = 1; i <= query.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return i;
            }

            trace.Add("stream:handler:end");
        }
    }

    private sealed class StreamBehavior(CallTrace trace)
        : IPipelineStreamBehavior<PipelineStream, int>
    {
        public IAsyncEnumerable<int> Handle(
            object? key,
            PipelineStream message,
            PipelineBehaviorDelegate<PipelineStream, IAsyncEnumerable<int>> next,
            CancellationToken cancellationToken = default
        ) => Execute(key, message, next, cancellationToken);

        private async IAsyncEnumerable<int> Execute(
            object? key,
            PipelineStream message,
            PipelineBehaviorDelegate<PipelineStream, IAsyncEnumerable<int>> next,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            trace.Add("stream:pre");
            await foreach (
                var item in next(key, message, cancellationToken)
                    .WithCancellation(cancellationToken)
            )
                yield return item;
            trace.Add("stream:post");
        }
    }

    private sealed class PipelineNotificationHandler1(CallTrace trace)
        : INotificationHandler<PipelineNotification>
    {
        public Task Handle(
            PipelineNotification notification,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("notification:handler:1");
            return Task.CompletedTask;
        }
    }

    private sealed class PipelineNotificationHandler2(CallTrace trace)
        : INotificationHandler<PipelineNotification>
    {
        public Task Handle(
            PipelineNotification notification,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("notification:handler:2");
            return Task.CompletedTask;
        }
    }

    private sealed class NotificationBehavior(CallTrace trace)
        : IPipelineBehavior<PipelineNotification, Task>
    {
        public async Task Handle(
            object? key,
            PipelineNotification message,
            PipelineBehaviorDelegate<PipelineNotification, Task> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("notification:pre");
            await next(key, message, cancellationToken);
            trace.Add("notification:post");
            trace.Add("notification:after-await");
        }
    }

    private sealed class ShorthandNotificationBehavior(CallTrace trace)
        : IPipelineNotificationBehavior<PipelineNotification>
    {
        public async Task Handle(
            object? key,
            PipelineNotification message,
            PipelineBehaviorDelegate<PipelineNotification, Task> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("notification:pre");
            await next(key, message, cancellationToken);
            trace.Add("notification:post");
            trace.Add("notification:after-await");
        }
    }
}
