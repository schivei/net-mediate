# Samples (API / Worker / Minimal API)

## API sample

```csharp
// Handlers (CreateOrderHandler, OrderCreatedEventHandler) are discovered
// and registered automatically by the source generator.
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetMediateGenerated();

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
builder.Services.AddNetMediateGenerated();
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
builder.Services.AddNetMediateGenerated();

var app = builder.Build();
app.MapPost("/orders", async (IMediator mediator, CreateOrder command, CancellationToken ct) =>
{
    var created = await mediator.Request<CreateOrder, OrderCreated>(command, ct);
    return Results.Ok(created);
});

app.Run();
```
