using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quartz;

namespace NetMediate.Quartz.Tests;

public class QuartzFixture : IDisposable
{
    private bool disposedValue;

    public Dictionary<string, string?> Configuration { get; } = [];

    private readonly Lazy<ServiceProvider> _serviceProviderLazy;

    public IServiceProvider ServiceProvider => _serviceProviderLazy.Value;

    private readonly IServiceCollection _services = new ServiceCollection();

    public IServiceCollection Services => _services;

    public QuartzFixture()
    {
        Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "Testing");

        Configuration["NetMediate:HandlersAssembly"] = typeof(QuartzFixture).Assembly.FullName;

        _serviceProviderLazy = new(Setup);
    }

    private ServiceProvider Setup()
    {
        var configuration = new ConfigurationManager()
            .AddEnvironmentVariables()
            .AddInMemoryCollection(Configuration);

        _services.AddSingleton<IConfiguration>(configuration.Build());

        _services.AddLogging();
        ConfigureQuartz(_services);

        return _services.BuildServiceProvider();
    }

    public static void ConfigureQuartz(IServiceCollection services)
    {
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

        services.AddNetMediate();
        services.AddNetMediateQuartz();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Configuration.Clear();
                if (_serviceProviderLazy.IsValueCreated)
                {
                    if (ServiceProvider is IAsyncDisposable asyncDisposable)
                        asyncDisposable.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    else if (ServiceProvider is IDisposable disposable)
                        disposable.Dispose();
                }
            }

            disposedValue = true;
        }
    }

    ~QuartzFixture()
    {
        Dispose(disposing: false);
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
