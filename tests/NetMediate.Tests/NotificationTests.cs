using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class NotificationTests
{
    private static async Task NotificationHandle<T>(
        IEnumerable<T> values = null!
    ) where T : BaseMessage, INotification
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

    private static async Task NotificationHandle<T>(T message, bool expected)
        where T : BaseMessage, INotification
    {
        await NotificationHandle([message]);
        Assert.Equal(expected, message.Runned);
    }

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
        NotificationHandle(new MessageNotification(1), true);
}
