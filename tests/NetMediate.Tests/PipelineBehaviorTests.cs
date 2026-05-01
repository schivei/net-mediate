using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests;

public sealed class PipelineBehaviorTests
{
    [Fact]
    public async Task RequestBehavior_ShouldRunInOrderAndWrapResponse()
    {
        using var host = await CreateHostAsync(
            services =>
            {
                services.AddScoped<IRequestBehavior<PipelineRequest, string>, FirstRequestBehavior>();
                services.AddScoped<IRequestBehavior<PipelineRequest, string>, SecondRequestBehavior>();
                services.AddSingleton<CallTrace>();
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();
        var trace = host.Services.GetRequiredService<CallTrace>();
        var cancellationToken = TestContext.Current.CancellationToken;

        var response = await mediator.Request<PipelineRequest, string>(
            new PipelineRequest("ok"),
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
        using var host = await CreateHostAsync(
            services =>
            {
                services.AddScoped<ICommandBehavior<PipelineCommand>, CommandBehavior>();
                services.AddSingleton<CallTrace>();
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();
        var trace = host.Services.GetRequiredService<CallTrace>();
        var cancellationToken = TestContext.Current.CancellationToken;

        await mediator.Send(new PipelineCommand("ok"), cancellationToken);

        Assert.Equal(
            ["command:pre", "command:handler", "command:post"],
            trace.ToArray()
        );
    }

    [Fact]
    public async Task StreamBehavior_ShouldWrapFullStreamExecution()
    {
        using var host = await CreateHostAsync(
            services =>
            {
                services.AddScoped<IStreamBehavior<PipelineStream, int>, StreamBehavior>();
                services.AddSingleton<CallTrace>();
            }
        );

        var mediator = host.Services.GetRequiredService<IMediator>();
        var trace = host.Services.GetRequiredService<CallTrace>();
        var cancellationToken = TestContext.Current.CancellationToken;

        var values = await mediator
            .RequestStream<PipelineStream, int>(new PipelineStream(3), cancellationToken)
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
        using var host = await CreateHostAsync(
            services =>
            {
                services.AddScoped<INotificationBehavior<PipelineNotification>, NotificationBehavior>();
                services.AddSingleton<CallTrace>();
            }
        );

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

        Assert.Fail(
            $"Timed out waiting for trace size {expected}. Current size: {trace.Count}."
        );
    }

    private static async Task<IHost> CreateHostAsync(Action<IServiceCollection> configure)
    {
        var builder = Host.CreateApplicationBuilder();
        configure(builder.Services);
        builder.Services.AddNetMediate(typeof(PipelineBehaviorTests).Assembly);

        var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        return host;
    }

    public sealed record PipelineRequest(string Value) : IRequest<string>;
    public sealed record PipelineCommand(string Value) : ICommand;
    public sealed record PipelineNotification(string Value) : INotification;
    public sealed record PipelineStream(int Count) : IStream<int>;

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
        public ValueTask<string> Handle(
            PipelineRequest query,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("request:handler");
            return ValueTask.FromResult(query.Value);
        }
    }

    private sealed class FirstRequestBehavior(CallTrace trace)
        : IRequestBehavior<PipelineRequest, string>
    {
        public async ValueTask<string> Handle(
            PipelineRequest message,
            RequestHandlerDelegate<PipelineRequest, string> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("request:first:pre");
            var response = await next(message, cancellationToken);
            trace.Add("request:first:post");
            return $"{response}:first";
        }
    }

    private sealed class SecondRequestBehavior(CallTrace trace)
        : IRequestBehavior<PipelineRequest, string>
    {
        public async ValueTask<string> Handle(
            PipelineRequest message,
            RequestHandlerDelegate<PipelineRequest, string> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("request:second:pre");
            var response = await next(message, cancellationToken);
            trace.Add("request:second:post");
            return $"{response}:second";
        }
    }

    private sealed class PipelineCommandHandler(CallTrace trace)
        : ICommandHandler<PipelineCommand>
    {
        public ValueTask Handle(PipelineCommand command, CancellationToken cancellationToken = default)
        {
            trace.Add("command:handler");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CommandBehavior(CallTrace trace)
        : ICommandBehavior<PipelineCommand>
    {
        public async ValueTask Handle(
            PipelineCommand message,
            CommandHandlerDelegate<PipelineCommand> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("command:pre");
            await next(message, cancellationToken);
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
        : IStreamBehavior<PipelineStream, int>
    {
        public IAsyncEnumerable<int> Handle(
            PipelineStream message,
            StreamHandlerDelegate<PipelineStream, int> next,
            CancellationToken cancellationToken = default
        ) => Execute(message, next, cancellationToken);

        private async IAsyncEnumerable<int> Execute(
            PipelineStream message,
            StreamHandlerDelegate<PipelineStream, int> next,
            [EnumeratorCancellation] CancellationToken cancellationToken
        )
        {
            trace.Add("stream:pre");
            await foreach (var item in next(message, cancellationToken).WithCancellation(cancellationToken))
                yield return item;
            trace.Add("stream:post");
        }
    }

    private sealed class PipelineNotificationHandler1(CallTrace trace)
        : INotificationHandler<PipelineNotification>
    {
        public ValueTask Handle(
            PipelineNotification notification,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("notification:handler:1");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class PipelineNotificationHandler2(CallTrace trace)
        : INotificationHandler<PipelineNotification>
    {
        public ValueTask Handle(
            PipelineNotification notification,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("notification:handler:2");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NotificationBehavior(CallTrace trace)
        : INotificationBehavior<PipelineNotification>
    {
        public async ValueTask Handle(
            PipelineNotification message,
            NotificationHandlerDelegate<PipelineNotification> next,
            CancellationToken cancellationToken = default
        )
        {
            trace.Add("notification:pre");
            await next(message, cancellationToken);
            trace.Add("notification:post");
            trace.Add("notification:after-await");
        }
    }
}
