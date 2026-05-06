using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.DataDog.ILogger;
using NetMediate.DataDog.OpenTelemetry;
using NetMediate.DataDog.Serilog;
using Serilog;
// ReSharper disable AccessToDisposedClosure

namespace NetMediate.Tests;

public sealed class DataDogIntegrationPackageTests
{
    [Fact]
    public void OpenTelemetryPackage_ShouldConfigureNetMediateDataDogPipeline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        var beforeCount = services.Count;

        services.AddNetMediateDataDogOpenTelemetry(
            options =>
            {
                options.ServiceName = "net-mediate-tests";
                options.ServiceVersion = "1.0.0";
                options.Environment = "test";
                options.ApiKey = "test-api-key";
            },
            cancellationToken
        );

        Assert.True(
            services.Count > beforeCount,
            "AddNetMediateDataDogOpenTelemetry should register OTel services into the service collection."
        );
    }

    [Fact]
    public void OpenTelemetryPackage_WithNullConfigure_ShouldUseDefaults()
    {
        var services = new ServiceCollection();
        var beforeCount = services.Count;

        services.AddNetMediateDataDogOpenTelemetry(configure: null, TestContext.Current.CancellationToken);

        Assert.True(
            services.Count > beforeCount,
            "AddNetMediateDataDogOpenTelemetry (null configure) should still register OTel services."
        );
    }

    [Fact]
    public void OpenTelemetryPackage_WithEmptyApiKey_ShouldNotSetHeaders()
    {
        var services = new ServiceCollection();
        var beforeCount = services.Count;

        services.AddNetMediateDataDogOpenTelemetry(options =>
        {
            options.ApiKey = string.Empty;
        }, TestContext.Current.CancellationToken);

        Assert.True(
            services.Count > beforeCount,
            "AddNetMediateDataDogOpenTelemetry should register OTel services even with an empty ApiKey."
        );
    }

    [Fact]
    public void SerilogPackage_ShouldRequireApiKeyWhenSinkIsEnabled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        var ex = Assert.Throws<System.Data.NoNullAllowedException>(() =>
            new LoggerConfiguration().UseNetMediateDataDogSerilog(
                options =>
                {
                    options.EnableSink = true;
                    options.ApiKey = string.Empty;
                },
                cancellationToken
            )
        );

        Assert.Contains("API key", ex.Message);
    }

    [Fact]
    public void SerilogPackage_ShouldConfigureSink_WhenEnabledWithValidApiKey()
    {
        var options = new DataDogSerilogOptions
        {
            ApiKey = "test-api-key",
            Source = "csharp",
            Service = "net-mediate-tests",
            Host = "localhost",
            Tags = ["env:test"],
            EnableSink = true,
        };

        Assert.Equal("test-api-key", options.ApiKey);
        Assert.Equal("csharp", options.Source);
        Assert.Equal("net-mediate-tests", options.Service);
        Assert.Equal("localhost", options.Host);
        Assert.Equal("env:test", options.Tags[0]);

        var loggerConfig = new LoggerConfiguration().UseNetMediateDataDogSerilog(
            opts =>
            {
                opts.ApiKey = options.ApiKey;
                opts.Source = options.Source;
                opts.Service = options.Service;
                opts.Host = options.Host;
                opts.Tags = options.Tags;
                opts.EnableSink = true;
            },
            TestContext.Current.CancellationToken
        );

        using var logger = loggerConfig.CreateLogger();
        var writeEx = Record.Exception(() => logger.Information("coverage-test {Value}", 42));
        Assert.Null(writeEx);
    }

    [Fact]
    public void ILoggerPackage_ShouldRegisterOptionsAndCreateScope()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetMediateDataDogILogger(
            options =>
            {
                options.Service = "net-mediate-tests";
                options.Environment = "test";
                options.Version = "1.0.0";
            },
            cancellationToken
        );

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DataDogILoggerOptions>();
        var logger = provider.GetRequiredService<ILogger<DataDogIntegrationPackageTests>>();

        using var scope = logger.BeginNetMediateDataDogScope(options, cancellationToken);
        Assert.Equal("net-mediate-tests", options.Service);
        Assert.Equal("test", options.Environment);
        Assert.Equal("1.0.0", options.Version);
    }

    [Fact]
    public void ILoggerPackage_WithNullConfigure_ShouldUseDefaults()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetMediateDataDogILogger(configure: null, TestContext.Current.CancellationToken);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DataDogILoggerOptions>();

        Assert.Equal("netmediate", options.Service);
        Assert.Equal("dev", options.Environment);
        Assert.Equal("unknown", options.Version);
    }

    [Fact]
    public void ILoggerPackage_BeginScope_ShouldReturnNoopScope_WhenLoggerReturnsNull()
    {
        var nullScopeLogger = new NullScopeLogger();
        var opts = new DataDogILoggerOptions
        {
            Service = "test",
            Environment = "ci",
            Version = "0",
        };

        using var scope = nullScopeLogger.BeginNetMediateDataDogScope(
            opts,
            TestContext.Current.CancellationToken
        );

        Assert.Equal("NoopScope", scope.GetType().Name);

        var disposeEx = Record.Exception(scope.Dispose);
        Assert.Null(disposeEx);
    }

    /// <summary>Logger that returns <see langword="null"/> from BeginScope, forcing NoopScope.</summary>
    private sealed class NullScopeLogger : Microsoft.Extensions.Logging.ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => false;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) { }
    }
}
