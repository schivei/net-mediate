using Microsoft.Extensions.DependencyInjection;
using NetMediate.Quartz.Tests.Messages;
using Quartz;
using System.Threading.Channels;

namespace NetMediate.Quartz.Tests;

public sealed class QuartzMediatorTests : IClassFixture<QuartzFixture>
{
    private readonly QuartzFixture _fixture;

    private readonly Channel<TestMessage> _keyedMultipleMessageSuccessfulHandlingChannel = Channel.CreateUnbounded<TestMessage>();

    public QuartzMediatorTests(QuartzFixture fixture)
    {
        fixture.Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.MisfireRetryCount)}"] = "3";
        _fixture = fixture;
        _fixture.Services.AddKeyedSingleton("keyed", _keyedMultipleMessageSuccessfulHandlingChannel.Writer);
    }

    [Fact]
    public async Task Keyed_MultipleMessage_SuccessfulHandling()
    {
        var serviceProvider = _fixture.ServiceProvider;
        TestMessage[] msgs = [
            new(1),
            new(2),
            new(3)
        ];

        var reader = _keyedMultipleMessageSuccessfulHandlingChannel.Reader;

        var mediator = serviceProvider.GetRequiredService<IMediator>();

        Assert.NotNull(mediator);

        var schedulerFactory = serviceProvider.GetRequiredService<ISchedulerFactory>();
        var scheduler = await schedulerFactory.GetScheduler(TestContext.Current.CancellationToken);

        await scheduler.Start(TestContext.Current.CancellationToken);

        mediator.Notifies("keyed", msgs);

        await scheduler.ResumeAll(TestContext.Current.CancellationToken);

        Assert.True(await reader.WaitToReadAsync(TestContext.Current.CancellationToken));

        await scheduler.Shutdown(true, TestContext.Current.CancellationToken);

        Assert.Equal(1, msgs[0].CheckValue);
        Assert.Equal(2, msgs[1].CheckValue);
        Assert.Equal(3, msgs[2].CheckValue);
        Assert.True(await reader.ReadAllAsync(TestContext.Current.CancellationToken).AllAsync(m => m.CheckValue > 0, TestContext.Current.CancellationToken));
    }
}
