using Microsoft.Extensions.DependencyInjection;
using NetMediate.Tests.Messages;

namespace NetMediate.Tests;

public sealed class CommandTests
{
    [Fact]
    public async Task MessageCommand_Handle_ShouldCompleteSuccessfully()
    {
        var message = new MessageCommand(10);

        using var fixture = new NetMediateFixture();
        await fixture.RunAsync(
            async (sp) =>
            {
                var mediator = sp.GetRequiredService<IMediator>();
                await mediator.Send(message, fixture.CancellationTokenSource.Token);
            }
        );
        await fixture.WaitAsync();
        Assert.True(message.Runned);
        Assert.Null(fixture.RunError);
    }
}
