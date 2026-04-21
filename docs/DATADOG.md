# NetMediate DataDog Integrations

NetMediate now provides dedicated optional packages for DataDog-focused observability scenarios.

## Packages

- `NetMediate.DataDog.OpenTelemetry`
- `NetMediate.DataDog.Serilog`
- `NetMediate.DataDog.ILogger`

All three runtime packages are multi-targeted for:

- `net10.0`
- `netstandard2.0`
- `netstandard2.1`

## OpenTelemetry package (`NetMediate.DataDog.OpenTelemetry`)

This package wires NetMediate built-in diagnostics:

- `ActivitySource`: `NetMediate`
- `Meter`: `NetMediate`

to OTLP exporters typically consumed by the DataDog Agent.

```csharp
using NetMediate.DataDog.OpenTelemetry;

builder.Services.AddNetMediateDataDogOpenTelemetry(options =>
{
    options.ServiceName = "my-service";
    options.ServiceVersion = "1.0.0";
    options.Environment = "prod";
    options.OtlpEndpoint = new Uri("http://localhost:4318");
    options.ApiKey = "<DATADOG_API_KEY>"; // optional
});
```

## Serilog package (`NetMediate.DataDog.Serilog`)

This package enriches logs with NetMediate fields and can attach the DataDog Serilog sink.

```csharp
using NetMediate.DataDog.Serilog;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .UseNetMediateDataDogSerilog(options =>
    {
        options.ApiKey = "<DATADOG_API_KEY>";
        options.Service = "my-service";
        options.Source = "csharp";
        options.Tags = ["team:platform", "env:prod"];
    })
    .CreateLogger();
```

## ILogger package (`NetMediate.DataDog.ILogger`)

This package adds DataDog-compatible scope helpers for `Microsoft.Extensions.Logging.ILogger`.

```csharp
using NetMediate.DataDog.ILogger;

builder.Services.AddNetMediateDataDogILogger(options =>
{
    options.Service = "my-service";
    options.Environment = "prod";
    options.Version = "1.0.0";
});

var logger = app.Services.GetRequiredService<ILogger<Program>>();
var ddOptions = app.Services.GetRequiredService<DataDogILoggerOptions>();
using var scope = logger.BeginNetMediateDataDogScope(ddOptions);
logger.LogInformation("Application started");
```
