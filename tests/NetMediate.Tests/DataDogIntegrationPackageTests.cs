using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.DataDog.ILogger;
using NetMediate.DataDog.OpenTelemetry;
using NetMediate.DataDog.Serilog;
using Serilog;

namespace NetMediate.Tests;

public sealed class DataDogIntegrationPackageTests
{
    [Fact]
    public void OpenTelemetryPackage_ShouldConfigureNetMediateDataDogPipeline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();

        services.AddNetMediateDataDogOpenTelemetry(options =>
        {
            options.ServiceName = "netmediate-tests";
            options.ServiceVersion = "1.0.0";
            options.Environment = "test";
            options.ApiKey = "test-api-key";
        }, cancellationToken);

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider);
    }

    [Fact]
    public void SerilogPackage_ShouldAllowConfigurationWithoutSink()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var logger = new LoggerConfiguration()
            .UseNetMediateDataDogSerilog(options =>
            {
                options.ApiKey = "test-api-key";
                options.EnableSink = false;
            }, cancellationToken)
            .CreateLogger();

        logger.Information("datadog serilog test");
        logger.Dispose();
    }

    [Fact]
    public void ILoggerPackage_ShouldRegisterOptionsAndCreateScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetMediateDataDogILogger(options =>
        {
            options.Service = "netmediate-tests";
            options.Environment = "test";
            options.Version = "1.0.0";
        }, cancellationToken);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DataDogILoggerOptions>();
        var logger = provider.GetRequiredService<ILogger<DataDogIntegrationPackageTests>>();

        using var scope = logger.BeginNetMediateDataDogScope(options, cancellationToken);
        Assert.NotNull(scope);
        Assert.Equal("netmediate-tests", options.Service);
    }
}
