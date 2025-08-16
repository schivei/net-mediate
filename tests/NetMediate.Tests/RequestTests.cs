using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class RequestTests
{
    private static async Task RequestHandle<TMessage>(
        TMessage message,
        bool expected,
        bool required = true
    )
        where TMessage : BaseMessage
    {
        using var fixture = new NetMediateFixture();

        // Act
        var response = await fixture.RunAsync(
            async (sp) =>
            {
                var mediator = sp.GetRequiredService<IMediator>();
                return await mediator.Request<TMessage, string>(
                    message,
                    fixture.CancellationTokenSource.Token
                );
            }
        );

        // Assert
        Assert.Equal(expected, message.Runned);

        if (expected)
        {
            Assert.Null(fixture.RunError);
            Assert.Equal("right", response);
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
    public Task DecoupledRequestHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => RequestHandle(new DecoupledValidatableMessage(name), expected, false);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1RequestHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => RequestHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed1ValidatableRequestHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => RequestHandle(new Keyed1ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2RequestHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => RequestHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task Keyed2ValidatableRequestHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => RequestHandle(new Keyed2ValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleRequestHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => RequestHandle(new SimpleValidatableMessage(name), expected);

    [Theory]
    [InlineData("right", true)]
    [InlineData("wrong", false)]
    public Task SimpleValidatableRequestHandler_Handle_ShouldCompleteSuccessfully(
        string name,
        bool expected
    ) => RequestHandle(new SimpleValidatableMessage(name), expected);
}
