using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class RequestTests
{
    [Theory]
    [InlineData(10, true)]
    [InlineData(1, false)]
    public async Task MessageRequest_Handle_ShouldCompleteSuccessfully(int id, bool expected)
    {
        var message = new MessageRequest(id);

        using var fixture = new NetMediateFixture();
        // Act
        var response = await fixture.RunAsync(
            async (sp) =>
            {
                var mediator = sp.GetRequiredService<IMediator>();
                return await mediator.Request<MessageRequest, int>(
                    message,
                    fixture.CancellationTokenSource.Token
                );
            }
        );
        // Assert
        if (expected)
        {
            Assert.Null(fixture.RunError);
            Assert.Equal(10, response);
        }
        else
        {
            Assert.Null(fixture.RunError);
            Assert.NotEqual(10, response);
        }
    }
}
