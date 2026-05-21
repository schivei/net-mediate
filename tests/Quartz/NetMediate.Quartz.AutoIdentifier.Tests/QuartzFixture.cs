using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using System.Diagnostics.CodeAnalysis;

[assembly: ExcludeFromCodeCoverage]

namespace NetMediate.Quartz.AutoIdentifier.Tests;

public sealed class AutoIdentifierFixture : AQuartzFixture
{
    public AutoIdentifierFixture()
    {
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.MisfireRetryCount)}"] = "1";
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.IdGenerationStrategy)}"] = "0";
    }
}

public sealed class HashIdentifierFixture : AQuartzFixture
{
    public HashIdentifierFixture()
    {
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.MisfireRetryCount)}"] = "1";
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.IdGenerationStrategy)}"] = "1";
    }
}

public sealed class GuidIdentifierFixture : AQuartzFixture
{
    public GuidIdentifierFixture()
    {
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.MisfireRetryCount)}"] = "1";
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.IdGenerationStrategy)}"] = "2";
    }
}

public sealed class MessageIdentifierFixture : AQuartzFixture
{
    public MessageIdentifierFixture()
    {
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.MisfireRetryCount)}"] = "1";
        Configuration[$"{nameof(QuartzNotificationOptions)}:{nameof(QuartzNotificationOptions.IdGenerationStrategy)}"] = "3";
    }
}

public abstract class AQuartzFixture
{
    private bool disposedValue;

    public Dictionary<string, string?> Configuration { get; } = [];

    public IServiceProvider ServiceProvider { get; private set; } = null!;

    public IScheduler Scheduler { get; private set; } = default!;

    public ITestNotifier TestNotifier { get; private set; } = null!;

    public IMediator Mediator { get; private set; } = null!;

    protected AQuartzFixture()
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");

        Configuration["NetMediate:HandlersAssembly"] = typeof(AQuartzFixture).Assembly.FullName;
    }

    protected virtual async Task Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Configuration.Clear();

                if (Scheduler != null)
                    await Scheduler.Shutdown(waitForJobsToComplete: true);
                if (ServiceProvider is IAsyncDisposable a)
                    await a.DisposeAsync();
                else if (ServiceProvider is IDisposable d)
                    d.Dispose();
            }

            disposedValue = true;
        }
    }

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
