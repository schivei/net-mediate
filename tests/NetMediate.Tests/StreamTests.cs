using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

    [Fact]
    public async Task StreamFanOut_MultipleHandlers_ShouldMergeAllStreamsSequentially()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Services.UseNetMediate(reg =>
        {
            reg.RegisterStreamHandler<FanOutStreamHandlerA, FanOutMessage, int>();
            reg.RegisterStreamHandler<FanOutStreamHandlerB, FanOutMessage, int>();
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        var results = await mediator
            .RequestStream<FanOutMessage, int>(new FanOutMessage(), TestContext.Current.CancellationToken)
            .AsyncToSync();

        // HandlerA yields 1, 2, 3 then HandlerB yields 4, 5, 6 — sequential fan-out.
        Assert.Equal([1, 2, 3, 4, 5, 6], [.. results]);
    }

    private sealed record FanOutMessage;

    private sealed class FanOutStreamHandlerA : IStreamHandler<FanOutMessage, int>
    {
        public IAsyncEnumerable<int> Handle(
            FanOutMessage message,
            CancellationToken cancellationToken = default)
        {
            return GetAsync();
            static async IAsyncEnumerable<int> GetAsync()
            {
                yield return 1;
                yield return 2;
                yield return 3;
                await Task.Yield();
            }
        }
    }

    private sealed class FanOutStreamHandlerB : IStreamHandler<FanOutMessage, int>
    {
        public IAsyncEnumerable<int> Handle(
            FanOutMessage message,
            CancellationToken cancellationToken = default)
        {
            return GetAsync();
            static async IAsyncEnumerable<int> GetAsync()
            {
                yield return 4;
                yield return 5;
                yield return 6;
                await Task.Yield();
            }
        }
    }
}
