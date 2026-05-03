using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

#if MEDIATR_COMPAT
using MediatR;
using ParityMediator = MediatR.IMediator;
#else
using NetMediate;
using NetMediate.Internals;
using ParityMediator = NetMediate.IMediator;
using Notifier = NetMediate.Moq.Notifier;
#endif

namespace SharedParity.Shared;

public class SharedMediatorParityTests
{
    [Fact]
    public async Task SharedFlow_ShouldBehaveTheSameAcrossMediatorPackages()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<DispatchRecorder>();

#if MEDIATR_COMPAT
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SharedMediatorParityTests>());
#else
        builder.Services.AddNetMediate<Notifier>(configure =>
        {
            configure.RegisterRequestHandler<PingRequestHandler, PingRequest, PingResponse>();
            configure.RegisterCommandHandler<AuditCommandHandler, AuditCommand>();
            configure.RegisterNotificationHandler<PingNotificationHandler, PingNotification>();
            configure.RegisterStreamHandler<CounterStreamHandler, CounterStream, int>();
        });
#endif

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<ParityMediator>();
        var recorder = host.Services.GetRequiredService<DispatchRecorder>();

#if MEDIATR_COMPAT
        var reply = await mediator.Send(new PingRequest("ping"), TestContext.Current.CancellationToken);
        await mediator.Send(new AuditCommand("command"), TestContext.Current.CancellationToken);
        await mediator.Publish(new PingNotification("notify"), TestContext.Current.CancellationToken);

        var streamed = new List<int>();
        await foreach (
            var item in mediator.CreateStream(new CounterStream(3), TestContext.Current.CancellationToken)
        ) streamed.Add(item);
#else
        var reply = await mediator.Request<PingRequest, PingResponse>(
            new("ping"),
            TestContext.Current.CancellationToken
        );

        await mediator.Send(new AuditCommand("command"), TestContext.Current.CancellationToken);

        await mediator.Notify(
            new PingNotification("notify"),
            TestContext.Current.CancellationToken
        );

        var streamed = new List<int>();
        await foreach (
            var item in mediator.RequestStream<CounterStream, int>(
                new(3),
                TestContext.Current.CancellationToken
            )
        )
            streamed.Add(item);
#endif

        Assert.NotNull(reply);
        Assert.Equal("ping:pong", reply.Value);
        Assert.Equal([0, 1, 2], streamed);
        Assert.Equal("command", recorder.LastCommand);

        var delivered = await WaitUntilAsync(
            () => recorder.LastNotification == "notify",
            TestContext.Current.CancellationToken
        );
        Assert.True(delivered);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    public sealed class DispatchRecorder
    {
        public string? LastCommand { get; set; }

        public string? LastNotification { get; set; }
    }

#if MEDIATR_COMPAT
    public sealed record PingRequest(string Value) : IRequest<PingResponse>;

    public sealed record AuditCommand(string Value) : IRequest;

    public sealed record PingNotification(string Value) : INotification;

    public sealed record CounterStream(int Count) : IStreamRequest<int>;

    public sealed class PingRequestHandler : IRequestHandler<PingRequest, PingResponse>
#else
    public sealed record PingRequest(string Value);

    public sealed record AuditCommand(string Value);

    public sealed record PingNotification(string Value);

    public sealed record CounterStream(int Count);

    public sealed class PingRequestHandler : IRequestHandler<PingRequest, PingResponse>
#endif
    {
        public Task<PingResponse> Handle(PingRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new PingResponse($"{request.Value}:pong"));
    }

    public sealed record PingResponse(string Value);

#if MEDIATR_COMPAT
    public sealed class AuditCommandHandler(DispatchRecorder recorder) : IRequestHandler<AuditCommand>
#else
    public sealed class AuditCommandHandler(DispatchRecorder recorder) : ICommandHandler<AuditCommand>
#endif
    {
        private readonly DispatchRecorder _recorder = recorder;

        public Task Handle(AuditCommand request, CancellationToken cancellationToken = default)
        {
            _recorder.LastCommand = request.Value;
            return Task.CompletedTask;
        }
    }

#if MEDIATR_COMPAT
    public sealed class PingNotificationHandler(DispatchRecorder recorder)
        : INotificationHandler<PingNotification>
#else
    public sealed class PingNotificationHandler(DispatchRecorder recorder)
        : INotificationHandler<PingNotification>
#endif
    {
        private readonly DispatchRecorder _recorder = recorder;

        public Task Handle(
            PingNotification notification,
            CancellationToken cancellationToken = default
        )
        {
            _recorder.LastNotification = notification.Value;
            return Task.CompletedTask;
        }
    }

#if MEDIATR_COMPAT
    public sealed class CounterStreamHandler : IStreamRequestHandler<CounterStream, int>
#else
    public sealed class CounterStreamHandler : IStreamHandler<CounterStream, int>
#endif
    {
        public async IAsyncEnumerable<int> Handle(
            CounterStream request,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
                CancellationToken cancellationToken = default
        )
        {
            for (var index = 0; index < request.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return index;
            }
        }
    }

    private static async Task<bool> WaitUntilAsync(
        Func<bool> predicate,
        CancellationToken cancellationToken,
        int attempts = 40,
        int delayMilliseconds = 25
    )
    {
        for (var index = 0; index < attempts; index++)
        {
            if (predicate())
                return true;

            await Task.Delay(delayMilliseconds, cancellationToken);
        }

        return predicate();
    }
}
