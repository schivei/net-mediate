using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MediatR;
using CompatMediator = MediatR.IMediator;
using CompatPublisher = MediatR.IPublisher;
using CompatSender = MediatR.ISender;

namespace NetMediate.Tests.Compat;

public class MediatRCompatTests
{
    [Fact]
    public async Task AddMediatR_ShouldResolveMediatorContractsAndDispatchMessages()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<DispatchRecorder>();
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MediatRCompatTests>());

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<CompatMediator>();
        var sender = host.Services.GetRequiredService<CompatSender>();
        var publisher = host.Services.GetRequiredService<CompatPublisher>();

        Assert.Same(mediator, sender);
        Assert.Same(mediator, publisher);

        var reply = await mediator.Send(new PingRequest("ping"));
        Assert.Equal("ping:pong", reply.Value);

        await sender.Send(new VoidCommand("run"), TestContext.Current.CancellationToken);

        await publisher.Publish(new PingNotification("notify"), TestContext.Current.CancellationToken);

        var streamed = new List<int>();
        await foreach (var item in sender.CreateStream(
                           new CounterStream(3),
                           TestContext.Current.CancellationToken
                       ))
            streamed.Add(item);

        Assert.Equal([0, 1, 2], streamed);

        var recorder = host.Services.GetRequiredService<DispatchRecorder>();
        Assert.Equal("run", recorder.LastCommand);

        var delivered = await WaitUntilAsync(
            () => recorder.LastNotification == "notify",
            TestContext.Current.CancellationToken
        );
        Assert.True(delivered);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ObjectOverloads_ShouldDispatchByRuntimeType()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<DispatchRecorder>();
        builder.Services.AddMediatR(typeof(MediatRCompatTests).Assembly);

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);
        var mediator = host.Services.GetRequiredService<CompatMediator>();

        var response = await mediator.Send(
            (object)new PingRequest("obj"),
            TestContext.Current.CancellationToken
        );

        Assert.NotNull(response);
        Assert.Equal("obj:pong", Assert.IsType<PingResponse>(response).Value);

        var unit = await mediator.Send(
            (object)new VoidCommand("obj-run"),
            TestContext.Current.CancellationToken
        );
        Assert.Equal(Unit.Value, Assert.IsType<Unit>(unit));

        await mediator.Publish(
            (object)new PingNotification("obj-notify"),
            TestContext.Current.CancellationToken
        );

        var streamed = new List<object?>();
        await foreach (var item in mediator.CreateStream(
                           (object)new CounterStream(2),
                           TestContext.Current.CancellationToken
                       ))
            streamed.Add(item);

        Assert.Equal([0, 1], streamed);

        var recorder = host.Services.GetRequiredService<DispatchRecorder>();
        Assert.Equal("obj-run", recorder.LastCommand);

        var delivered = await WaitUntilAsync(
            () => recorder.LastNotification == "obj-notify",
            TestContext.Current.CancellationToken
        );
        Assert.True(delivered);

        await host.StopAsync(TestContext.Current.CancellationToken);
    }

    public sealed record PingRequest(string Value) : IRequest<PingResponse>;

    public sealed record PingResponse(string Value);

    public sealed record VoidCommand(string Value) : IRequest;

    public sealed record PingNotification(string Value) : INotification;

    public sealed record CounterStream(int Count) : IStreamRequest<int>;

    public sealed class DispatchRecorder
    {
        public string? LastCommand { get; set; }

        public string? LastNotification { get; set; }
    }

    public sealed class PingRequestHandler : IRequestHandler<PingRequest, PingResponse>
    {
        public Task<PingResponse> Handle(
            PingRequest request,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new PingResponse($"{request.Value}:pong"));
    }

    public sealed class VoidCommandHandler(DispatchRecorder recorder) : IRequestHandler<VoidCommand>
    {
        private readonly DispatchRecorder _recorder = recorder;

        public Task Handle(VoidCommand request, CancellationToken cancellationToken = default)
        {
            _recorder.LastCommand = request.Value;
            return Task.CompletedTask;
        }
    }

    public sealed class PingNotificationHandler(DispatchRecorder recorder)
        : INotificationHandler<PingNotification>
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

    public sealed class CounterStreamHandler : IStreamRequestHandler<CounterStream, int>
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
