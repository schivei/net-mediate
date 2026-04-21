using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests;

public sealed class SourceGenerationSupportTests
{
    [Fact]
    public async Task AddNetMediate_WithConfigureOverload_ShouldAllowExplicitRegistrationWithoutAssemblyScan()
    {
        ExplicitRegistrationCommandHandler.Executed = 0;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(registration =>
        {
            registration.RegisterCommandHandler<
                ExplicitRegistrationCommand,
                ExplicitRegistrationCommandHandler
            >();
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Send(new ExplicitRegistrationCommand(), TestContext.Current.CancellationToken);

        Assert.Equal(1, Volatile.Read(ref ExplicitRegistrationCommandHandler.Executed));
    }

    public sealed record ExplicitRegistrationCommand;

    private sealed class ExplicitRegistrationCommandHandler
        : ICommandHandler<ExplicitRegistrationCommand>
    {
        public static int Executed;

        public Task Handle(
            ExplicitRegistrationCommand command,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref Executed);
            return Task.CompletedTask;
        }
    }
}
