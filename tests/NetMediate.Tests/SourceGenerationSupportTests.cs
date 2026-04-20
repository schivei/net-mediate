using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NetMediate.Tests;

public sealed class SourceGenerationSupportTests
{
    [Fact]
    public async Task AddNetMediate_WithConfigureOverload_ShouldAllowExplicitRegistrationWithoutAssemblyScan()
    {
        GeneratedCommandHandler.Executed = false;

        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddNetMediate(registration =>
        {
            registration.RegisterCommandHandler<GeneratedCommand, GeneratedCommandHandler>();
        });

        using var host = builder.Build();
        await host.StartAsync(TestContext.Current.CancellationToken);

        var mediator = host.Services.GetRequiredService<IMediator>();
        await mediator.Send(new GeneratedCommand(), TestContext.Current.CancellationToken);

        Assert.True(GeneratedCommandHandler.Executed);
    }

    public sealed record GeneratedCommand;

    private sealed class GeneratedCommandHandler : ICommandHandler<GeneratedCommand>
    {
        public static bool Executed { get; set; }

        public Task Handle(GeneratedCommand command, CancellationToken cancellationToken = default)
        {
            Executed = true;
            return Task.CompletedTask;
        }
    }
}
