using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class NotificationTests
{
    private static async Task NotificationHandle<T>(T message, bool expected, bool required = true) where T : BaseMessage
    {
        using var fixture = new NetMediateFixture();

        await fixture.RunAsync(async (sp) =>
        {
            var mediator = sp.GetRequiredService<IMediator>();
            await mediator.Notify(message, fixture.CancellationTokenSource.Token);
            await Task.Delay(500);
        });

        // Act
        await fixture.WaitAsync();
        Assert.Equal(expected, message.Runned);

        if (expected)
        {
            Assert.Null(fixture.RunError);
        }
        else
        {
            Assert.NotNull(fixture.RunError);
            var ex = Assert.IsType<MessageValidationException>(fixture.RunError);
            var msg = required ? "Name is required" : "Name must be 'right'.";
            Assert.Equal(msg, ex.Message);
        }
    }

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task DecoupledNotificationHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        NotificationHandle(new DecoupledValidatableMessage(name), expected, false);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1NotificationHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        NotificationHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1ValidatableNotificationHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        NotificationHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2NotificationHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        NotificationHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2ValidatableNotificationHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        NotificationHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleNotificationHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        NotificationHandle(new SimpleValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleValidatableNotificationHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        NotificationHandle(new SimpleValidatableMessage(name), expected);
}
