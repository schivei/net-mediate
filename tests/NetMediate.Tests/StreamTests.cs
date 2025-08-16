using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class StreamTests
{
    private static async Task StreamHandle<TMessage>(TMessage message, bool expected, bool required = true) where TMessage : BaseMessage
    {
        using var fixture = new NetMediateFixture();
        DateTime.UtcNow.Subtract(DateTime.UtcNow.Date).TotalMilliseconds.ToString("N0")
        // Act
        var response = await fixture.RunAsync(async (sp) =>
        {
            var mediator = sp.GetRequiredService<IMediator>();
            return await mediator.RequestStream<TMessage, string>(message, fixture.CancellationTokenSource.Token).AsyncToSync();
        });

        // Assert
        if (expected)
        {
            Assert.Null(fixture.RunError);
            Assert.Equal(["right"], [.. response]);
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
    public Task DecoupledStreamHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        StreamHandle(new DecoupledValidatableMessage(name), expected, false);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1StreamHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        StreamHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1ValidatableStreamHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        StreamHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2StreamHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        StreamHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2ValidatableStreamHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        StreamHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleStreamHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        StreamHandle(new SimpleValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleValidatableStreamHandler_Handle_ShouldCompleteSuccessfully(string name, bool expected) =>
        StreamHandle(new SimpleValidatableMessage(name), expected);
}
