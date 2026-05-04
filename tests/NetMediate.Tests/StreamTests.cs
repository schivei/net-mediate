using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class StreamTests
{
    [Theory]
    [InlineData(10, true)]
    [InlineData(1, false)]
    public async Task MessageStream_Handle_ShouldCompleteSuccessfully(int id, bool expected)
    {
        var message = new MessageStream(id);

        using var fixture = new NetMediateFixture();
        // Act
        var response = await fixture.RunAsync(
            async (sp) =>
            {
                var mediator = sp.GetRequiredService<IMediator>();
                return await mediator
                    .RequestStream<MessageStream, int>(message, fixture.CancellationTokenSource.Token)
                    .AsyncToSync();
            }
        );
        // Assert
        if (expected)
        {
            Assert.Null(fixture.RunError);
            Assert.Equal(Enumerable.Range(0, 10), [.. response]);
        }
        else
        {
            Assert.Null(fixture.RunError);
            Assert.NotEqual(Enumerable.Range(0, 10), [.. response]);
        }
    }
}
