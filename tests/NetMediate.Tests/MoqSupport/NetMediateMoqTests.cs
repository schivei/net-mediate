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
        mock.Setup(service => service.GetValueTask()).ReturnsValueTask("ok-vt");

        await mock.Object.Execute();
        var response = await mock.Object.Get();
        var valueTaskResponse = await mock.Object.GetValueTask();

        Assert.Equal("ok", response);
        Assert.Equal("ok-vt", valueTaskResponse);
    }

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

        ValueTask<string> GetValueTask();
    }
}
