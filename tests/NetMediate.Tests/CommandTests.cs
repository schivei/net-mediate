using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class CommandTests
{
    private static async Task CommandHandle<T>(T message, bool expected, bool required = true) where T : BaseMessage
    {
        using var fixture = new NetMediateFixture();

        await fixture.RunAsync(async (sp) => {
            var mediator = sp.GetRequiredService<IMediator>();
            await mediator.Send(message, fixture.CancellationTokenSource.Token);
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
    public Task DecoupledCommandHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        CommandHandle(new DecoupledValidatableMessage(name), expected, false);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1CommandHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        CommandHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1ValidatableCommandHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        CommandHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2CommandHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        CommandHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2ValidatableCommandHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        CommandHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleCommandHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        CommandHandle(new SimpleValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleValidatableCommandHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        CommandHandle(new SimpleValidatableMessage(name), expected);
}
