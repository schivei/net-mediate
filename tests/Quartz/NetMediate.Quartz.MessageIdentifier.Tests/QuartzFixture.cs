using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using System.Diagnostics.CodeAnalysis;

[assembly: ExcludeFromCodeCoverage]

namespace NetMediate.Quartz.MessageIdentifier.Tests;

public sealed class MessageIdentifierFixture
{
    public MessageIdentifierFixture()
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");

        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.MisfireRetryCount)}"] = "1";
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.IdGenerationStrategy)}"] = "3";
    }

    public Dictionary<string, string?> Configuration { get; } = [];

    public IServiceProvider ServiceProvider { get; private set; } = null!;

    public IScheduler Scheduler { get; private set; } = default!;

    public ITestNotifier TestNotifier { get; private set; } = null!;

    public IMediator Mediator { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection();

        var configuration = new ConfigurationManager()
            .AddEnvironmentVariables()
            .AddInMemoryCollection(Configuration);

        services.AddSingleton<IConfiguration>(configuration.Build());
        services.AddLogging();
        services.AddSingleton<ITestNotifier, TestNotifier>();

        services.AddQuartz(q =>
        {
            q.SchedulerId = "AUTO";
            q.SchedulerName = "NetMediate";
            q.UseInMemoryStore();

            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = Environment.ProcessorCount);
        });

        services.AddQuartzHostedService(opt =>
        {
            opt.WaitForJobsToComplete = true;
            opt.AwaitApplicationStarted = true;
        });

        services.AddNetMediateQuartz();
        services.AddNetMediate();

        ServiceProvider = services.BuildServiceProvider();

        TestNotifier = ServiceProvider.GetRequiredService<ITestNotifier>();

        Mediator = ServiceProvider.GetRequiredService<IMediator>();

        var schedulerFactory = ServiceProvider.GetRequiredService<ISchedulerFactory>();
        Scheduler = await schedulerFactory.GetScheduler();
        await Scheduler.Start();
    }
}
