---
sidebar_position: 7
---

# Samples

> **GenDI pattern:** The examples below assume `NetMediate.SourceGeneration` in the startup project. Prefer `[Injectable]` + `[Inject]` for your services/handlers so the consumer can choose lifetime, group, order, key, and the preferred service contract.

Real-world integration samples for API, Worker, Minimal API, and keyed dispatch scenarios.

## API sample

```csharp
// Handlers (CreateOrderHandler, OrderCreatedEventHandler) are discovered
// and registered automatically by the source generator.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetMediate();

var app = builder.Build();
app.MapPost("/orders", async (IMediator mediator, CreateOrder command, CancellationToken ct) =>
{
    var created = await mediator.RequestCreateOrderAsync(command, ct);
    return Results.Ok(created);
});

app.Run();
```

## Worker sample

```csharp
// SyncCommandHandler is discovered and registered automatically by the source generator.
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNetMediate();
builder.Services.AddHostedService<Worker>();

await builder.Build().RunAsync();

[Injectable(ServiceLifetime.Singleton)]
public sealed class Worker : BackgroundService
{
    [Inject] public required IMediator Mediator { get; init; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Mediator.Send(new SyncCommand(), stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
```

## Minimal API sample

```csharp
// CreateOrderHandler is discovered and registered automatically by the source generator.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetMediate();

var app = builder.Build();
app.MapPost("/orders", async (IMediator mediator, CreateOrder command, CancellationToken ct) =>
{
    var created = await mediator.RequestCreateOrderAsync(command, ct);
    return Results.Ok(created);
});

app.Run();
```

## Keyed dispatch sample

Register handlers under routing keys and dispatch selectively at runtime. The `key` flows through the entire pipeline, making it available to every behavior for contextual decisions such as queue selection or tenant routing.

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetMediate();

var app = builder.Build();

// Dispatch to the default (null-key) handler
app.MapPost("/orders", async (IMediator mediator, ProcessOrder cmd, CancellationToken ct) =>
{
    await mediator.SendProcessOrderAsync(cmd, ct);
    return Results.Accepted();
});

// Dispatch to the "priority" handler
app.MapPost("/orders/priority", async (IMediator mediator, ProcessOrder cmd, CancellationToken ct) =>
{
    await mediator.SendProcessOrderAsync("priority", cmd, ct);
    return Results.Accepted();
});

app.Run();

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public sealed class DefaultOrderHandler : ICommandHandler<ProcessOrder>
{
    public Task Handle(ProcessOrder message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 2, Key = "priority")]
public sealed class PriorityOrderHandler : ICommandHandler<ProcessOrder>
{
    public Task Handle(ProcessOrder message, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
```

:::note Default routing key
A `null` key (the default when no key is passed) flows through the pipeline unchanged and targets the non-keyed handlers registered in the container.
:::

:::note NativeAOT
Both keyed and non-keyed dispatch are NativeAOT-compatible in the current NetMediate generator path. Keyed routing uses a source-generated `KeyedHandlerRegistry<T>` instead of `IKeyedServiceProvider`.
:::
