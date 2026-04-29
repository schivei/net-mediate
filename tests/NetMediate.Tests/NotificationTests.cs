using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class NotificationTests
{
    private static async Task NotificationHandle<T>(
        IEnumerable<T> values = null!
    )
        where T : BaseMessage
    {
        using var fixture = new NetMediateFixture();
        await fixture.RunAsync(
            async (sp) =>
            {
                var mediator = sp.GetRequiredService<IMediator>();
                await mediator.Notify(values, fixture.CancellationTokenSource.Token);
                await Task.Delay(500);
            }
        );
        // Act
        await fixture.WaitAsync();

        Assert.Null(fixture.RunError);
    }

    private static async Task NotificationsHandle<T>(
        IEnumerable<INotification<T>> values = null!
    )
        where T : BaseMessage, INotification<T>
    {
        using var fixture = new NetMediateFixture();
        await fixture.RunAsync(
            async (sp) =>
            {
                var mediator = sp.GetRequiredService<IMediator>();
                await mediator.Notify(values, fixture.CancellationTokenSource.Token);
                await Task.Delay(500);
            }
        );
        // Act
        await fixture.WaitAsync();

        Assert.Null(fixture.RunError);
    }

    private static async Task NotificationsHandle<T>(INotification<T> message, bool expected)
        where T : BaseMessage, INotification<T>
    {
        await NotificationsHandle([message]);
        Assert.Equal(expected, ((T)message).Runned);
    }

    private static async Task NotificationHandle<T>(T message, bool expected)
        where T : BaseMessage
    {
        await NotificationHandle([message]);
        Assert.Equal(expected, message.Runned);
    }

    [Fact]
    public async Task DecoupledNotificationHandler_Handle_ShouldCompleteSuccessfully_Collection()
    {
        IEnumerable<DecoupledValidatableMessage> c1 = [];
        IEnumerable<DecoupledValidatableMessage> c2 = null!;
        await NotificationHandle(c1);
        await NotificationHandle(c2);
    }

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task DecoupledNotificationHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => NotificationHandle(new DecoupledValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1NotificationHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => NotificationHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1ValidatableNotificationHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => NotificationHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2NotificationHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => NotificationHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2ValidatableNotificationHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => NotificationHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleNotificationHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => NotificationHandle(new SimpleValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleValidatableNotificationHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => NotificationHandle(new SimpleValidatableMessage(name), expected);

    [Fact]
    public Task MessageNotification_Handle_ShouldCompleteSuccessfully() =>
        NotificationsHandle(new MessageNotification(1), true);
}
