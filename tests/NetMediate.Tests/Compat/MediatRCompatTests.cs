using Microsoft.Extensions.DependencyInjection;
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
        var services = new ServiceCollection();
        services.AddSingleton<DispatchRecorder>();
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<MediatRCompatTests>());

        await using var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<CompatMediator>();
        var sender = provider.GetRequiredService<CompatSender>();
        var publisher = provider.GetRequiredService<CompatPublisher>();

        Assert.Same(mediator, sender);
        Assert.Same(mediator, publisher);

        var reply = await mediator.Send(new PingRequest("ping"));
        Assert.Equal("ping:pong", reply.Value);

        await sender.Send(new VoidCommand("run"));

        await publisher.Publish(new PingNotification("notify"));

        var streamed = new List<int>();
        await foreach (var item in sender.CreateStream(new CounterStream(3)))
            streamed.Add(item);

        Assert.Equal([0, 1, 2], streamed);

        var recorder = provider.GetRequiredService<DispatchRecorder>();
        Assert.Equal("run", recorder.LastCommand);
        Assert.Null(recorder.LastNotification);
    }

    [Fact]
    public async Task ObjectOverloads_ShouldDispatchByRuntimeType()
    {
        var services = new ServiceCollection();
        services.AddSingleton<DispatchRecorder>();
        services.AddMediatR(typeof(MediatRCompatTests).Assembly);

        await using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<CompatMediator>();

        var response = await mediator.Send((object)new PingRequest("obj"));

        Assert.NotNull(response);
        Assert.Equal("obj:pong", Assert.IsType<PingResponse>(response).Value);

        await mediator.Publish((object)new PingNotification("obj-notify"));

        var streamed = new List<object?>();
        await foreach (var item in mediator.CreateStream((object)new CounterStream(2)))
            streamed.Add(item);

        Assert.Equal([0, 1], streamed);

        var recorder = provider.GetRequiredService<DispatchRecorder>();
        Assert.Null(recorder.LastNotification);
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

}
