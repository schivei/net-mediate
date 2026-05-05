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
        var beforeCount = services.Count;

        services.AddNetMediateDataDogOpenTelemetry(
            options =>
            {
                options.ServiceName = "netmediate-tests";
                options.ServiceVersion = "1.0.0";
                options.Environment = "test";
                options.ApiKey = "test-api-key";
            },
            cancellationToken
        );

        // AddNetMediateDataDogOpenTelemetry must register OpenTelemetry services.
        Assert.True(
            services.Count > beforeCount,
            "AddNetMediateDataDogOpenTelemetry should register OTel services into the service collection."
        );
    }

    [Fact]
    public void OpenTelemetryPackage_WithNullConfigure_ShouldUseDefaults()
    {
        // Covers the configure?.Invoke(options) null branch.
        // When configure is null the default DataDogOpenTelemetryOptions values are used.
        var services = new ServiceCollection();
        var beforeCount = services.Count;

        services.AddNetMediateDataDogOpenTelemetry(configure: null);

        Assert.True(
            services.Count > beforeCount,
            "AddNetMediateDataDogOpenTelemetry (null configure) should still register OTel services."
        );
    }

    [Fact]
    public void OpenTelemetryPackage_WithEmptyApiKey_ShouldNotSetHeaders()
    {
        // Covers the !string.IsNullOrWhiteSpace(options.ApiKey) = false branch (empty/whitespace key).
        // The method should complete without throwing even when ApiKey is empty.
        var services = new ServiceCollection();
        var beforeCount = services.Count;

        services.AddNetMediateDataDogOpenTelemetry(options =>
        {
            options.ApiKey = string.Empty;
        });

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
        // Exercises the WriteTo.DatadogLogs(...) branch (lines inside EnableSink=true path).
        // The sink is only configured — no network connection is made during construction.
        var options = new DataDogSerilogOptions
        {
            ApiKey = "test-api-key",
            Source = "csharp",
            Service = "netmediate-tests",
            Host = "localhost",
            Tags = ["env:test"],
            EnableSink = true,
        };

        // Read back all option properties to ensure coverage of the options type.
        Assert.Equal("test-api-key", options.ApiKey);
        Assert.Equal("csharp", options.Source);
        Assert.Equal("netmediate-tests", options.Service);
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

        // CreateLogger() triggers the full Serilog pipeline build.
        // Verify no exception is raised when writing through the configured sink.
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
                options.Service = "netmediate-tests";
                options.Environment = "test";
                options.Version = "1.0.0";
            },
            cancellationToken
        );

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DataDogILoggerOptions>();
        var logger = provider.GetRequiredService<ILogger<DataDogIntegrationPackageTests>>();

        using var scope = logger.BeginNetMediateDataDogScope(options, cancellationToken);
        // Verify configured options are reflected in what was registered.
        Assert.Equal("netmediate-tests", options.Service);
        Assert.Equal("test", options.Environment);
        Assert.Equal("1.0.0", options.Version);
    }

    [Fact]
    public void ILoggerPackage_WithNullConfigure_ShouldUseDefaults()
    {
        // Covers the configure?.Invoke(options) null branch in AddNetMediateDataDogILogger.
        // When configure is null the class-level default values must remain unchanged.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNetMediateDataDogILogger(configure: null);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<DataDogILoggerOptions>();

        // Verify the hard-coded defaults defined in DataDogILoggerOptions.
        Assert.Equal("netmediate", options.Service);
        Assert.Equal("dev", options.Environment);
        Assert.Equal("unknown", options.Version);
    }

    [Fact]
    public void ILoggerPackage_BeginScope_ShouldReturnNoopScope_WhenLoggerReturnsNull()
    {
        // Uses a custom ILogger whose BeginScope returns null to exercise the
        // NoopScope.Instance fallback path in BeginNetMediateDataDogScope.
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

        // The returned scope must be the NoopScope singleton (private nested class).
        Assert.Equal("NoopScope", scope.GetType().Name);

        // Dispose must not throw (it is intentionally a no-op).
        var disposeEx = Record.Exception(() => scope.Dispose());
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
