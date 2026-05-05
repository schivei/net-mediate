using Microsoft.Extensions.DependencyInjection;
using NetMediate.Moq;

namespace NetMediate.Tests.MoqSupport;

public class NetMediateMoqTests
{
    [Fact]
    public void AddMockSingleton_ShouldReplaceServiceRegistrationAndResolveMock()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ISampleService, SampleService>();

        var mock = services.AddMockSingleton<ISampleService>();
        mock.Setup(service => service.Get()).Returns("mocked");

        using var provider = services.BuildServiceProvider();

        var resolvedMock = provider.GetRequiredService<global::Moq.Mock<ISampleService>>();
        var resolvedService = provider.GetRequiredService<ISampleService>();

        Assert.Same(mock, resolvedMock);
        Assert.Equal("mocked", resolvedService.Get());
    }

    [Fact]
    public async Task FluentExtensions_ShouldSupportAsyncSetups()
    {
        var mock = Mocking.Strict<IAsyncSample>();

        mock.Setup(service => service.Execute()).ReturnsCompletedTask();
        mock.Setup(service => service.Get()).ReturnsTaskResult("ok");
        mock.Setup(service => service.GetTask()).ReturnsTask("ok-vt");

        await mock.Object.Execute();
        var response = await mock.Object.Get();
        var TaskResponse = await mock.Object.GetTask();

        Assert.Equal("ok", response);
        Assert.Equal("ok-vt", TaskResponse);
    }

    [Fact]
    public void Mocking_Create_ShouldReturnDefaultBehaviorMock()
    {
        var mock = Mocking.Create<ISampleService>();
        Assert.Equal(global::Moq.MockBehavior.Default, mock.Behavior);
    }

    [Fact]
    public void Mocking_Loose_ShouldReturnLooseMock()
    {
        var mock = Mocking.Loose<ISampleService>();
        Assert.Equal(global::Moq.MockBehavior.Loose, mock.Behavior);
    }

    [Fact]
    public void AddMockSingleton_WithExistingMock_ShouldRegisterProvidedMock()
    {
        var services = new ServiceCollection();
        var existing = new global::Moq.Mock<ISampleService>();
        existing.Setup(s => s.Get()).Returns("pre-built");

        var returned = services.AddMockSingleton(existing);

        Assert.Same(existing, returned);

        using var provider = services.BuildServiceProvider();
        Assert.Equal("pre-built", provider.GetRequiredService<ISampleService>().Get());
    }

    [Fact]
    public async Task MoqNotifier_DispatchNotifications_ShouldInvokeAllHandlers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();

        var notifier = new Notifier(provider);

        var h1Called = false;
        var h2Called = false;

        var h1 = new LambdaNotificationHandler<NotifierTestMessage>(
            (_, _) =>
            {
                h1Called = true;
                return Task.CompletedTask;
            }
        );
        var h2 = new LambdaNotificationHandler<NotifierTestMessage>(
            (_, _) =>
            {
                h2Called = true;
                return Task.CompletedTask;
            }
        );

        await notifier.DispatchNotifications(
            null,
            new NotifierTestMessage(),
            [h1, h2],
            TestContext.Current.CancellationToken
        );

        Assert.True(h1Called);
        Assert.True(h2Called);
    }

    [Fact]
    public void AddMediatorMock_ShouldRegisterMediatorMock()
    {
        var services = new ServiceCollection();
        var mock = services.AddMediatorMock();

        Assert.NotNull(mock);

        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IMediator>();
        Assert.Same(mock.Object, resolved);
    }

    public sealed record NotifierTestMessage;

    public interface ISampleService
    {
        string Get();
    }

    public sealed class SampleService : ISampleService
    {
        public string Get() => "real";
    }

    public interface IAsyncSample
    {
        Task Execute();

        Task<string> Get();

        Task<string> GetTask();
    }

    private sealed class LambdaNotificationHandler<TMessage>(
        Func<TMessage, CancellationToken, Task> fn
    ) : INotificationHandler<TMessage>
        where TMessage : notnull
    {
        public Task Handle(TMessage message, CancellationToken cancellationToken = default) =>
            fn(message, cancellationToken);
    }
}
