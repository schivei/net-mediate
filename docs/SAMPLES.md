# Samples (API / Worker / Minimal API)

## API sample

```csharp
// Handlers (CreateOrderHandler, OrderCreatedEventHandler) are discovered
// and registered automatically by the source generator.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetMediate();

var app = builder.Build();
app.MapPost("/orders", async (IMediator mediator, CreateOrder command, CancellationToken ct) =>
{
    var created = await mediator.Request<CreateOrder, OrderCreated>(command, ct);
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

public sealed class Worker(IMediator mediator) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await mediator.Send(new SyncCommand(), stoppingToken);
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
    var created = await mediator.Request<CreateOrder, OrderCreated>(command, ct);
    return Results.Ok(created);
});

app.Run();
```

## Keyed dispatch sample

Register handlers under routing keys and dispatch selectively at runtime. The `key` flows through the entire pipeline, making it available to every behavior for contextual decisions such as queue selection or tenant routing.

```csharp
// Registration — handlers share a message type but differ by key
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterCommandHandler<DefaultOrderHandler, ProcessOrder>();        // null key
    configure.RegisterCommandHandler<PriorityOrderHandler, ProcessOrder>("priority"); // keyed
});

var app = builder.Build();

// Dispatch to the default (null-key) handler
app.MapPost("/orders", async (IMediator mediator, ProcessOrder cmd, CancellationToken ct) =>
{
    await mediator.Send(cmd, ct);
    return Results.Accepted();
});

// Dispatch to the "priority" handler
app.MapPost("/orders/priority", async (IMediator mediator, ProcessOrder cmd, CancellationToken ct) =>
{
    await mediator.Send("priority", cmd, ct);
    return Results.Accepted();
});

app.Run();
```

> **NativeAOT:** Keyed registration uses `IKeyedServiceProvider` internally and is **not NativeAOT-compatible**.
