# Samples (API / Worker / Minimal API)

## API sample

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetMediate(typeof(CreateOrderHandler).Assembly);

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
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNetMediate<Worker>();
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
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddNetMediate(typeof(CreateOrderHandler).Assembly);

var app = builder.Build();
app.MapPost("/orders", async (IMediator mediator, CreateOrder command, CancellationToken ct) =>
{
    var created = await mediator.Request<CreateOrder, OrderCreated>(command, ct);
    return Results.Ok(created);
});

app.Run();
```
