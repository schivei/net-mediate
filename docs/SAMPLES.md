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

## Minimal API sample with MediatR contracts via compat

```csharp
using MediatR;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>());

var app = builder.Build();
app.MapPost("/orders", async (IMediator mediator, CreateOrder command, CancellationToken ct)
    => Results.Ok(await mediator.Send(command, ct)));

app.Run();
```

## Channel-based fire-and-forget notifications (background worker)

Use `NetMediate.InternalNotifier` when you want `mediator.Notify(...)` to return
immediately without waiting for all handlers to finish:

```csharp
using NetMediate.InternalNotifier;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddNetMediate(typeof(OrderShippedHandler).Assembly)
    .AddNetMediateInternalNotifier(); // registers ChannelNotificationProvider + BackgroundNotificationWorker

var app = builder.Build();
app.MapPost("/orders/{id}/ship", async (IMediator mediator, Guid id, CancellationToken ct) =>
{
    // Returns as soon as the notification is enqueued.
    // Handlers run asynchronously in the background.
    await mediator.Notify(new OrderShipped(id), ct);
    return Results.Accepted();
});

app.Run();
```

## Source-generated registration sample (AOT-safe)

```csharp
using NetMediate;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddNetMediateGenerated(); // zero startup reflection

await builder.Build().RunAsync();
```

## Unit test sample with inline notification provider

Use `NetMediate.InternalNotifier.Test` to avoid `Task.Delay` in tests:

```csharp
using NetMediate;
using NetMediate.InternalNotifier.Test;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddNetMediate(typeof(MyHandler).Assembly)
        .AddNetMediateTestNotifier();

var sp = services.BuildServiceProvider();
var mediator = sp.GetRequiredService<IMediator>();

// Handlers execute synchronously and inline — no delay needed
await mediator.Notify(new UserCreated("user-1"));
```

## Resilience sample

```csharp
using NetMediate.Resilience;

var builder = WebApplication.CreateBuilder(args);
builder.Services
    .AddNetMediate(typeof(GetProductHandler).Assembly)
    .AddNetMediateResilience(
        configureRetry: o =>
        {
            o.MaxRetryCount = 3;
            o.Delay = TimeSpan.FromMilliseconds(200);
        },
        configureTimeout: o => o.RequestTimeout = TimeSpan.FromSeconds(5));
```

## Validation sample

### Self-validating message

```csharp
using System.ComponentModel.DataAnnotations;

public record CreateUser(string Email, string Name) : IValidatable
{
    public Task<ValidationResult> ValidateAsync()
    {
        if (string.IsNullOrWhiteSpace(Email) || !Email.Contains('@'))
            return Task.FromResult(new ValidationResult("Invalid email", new[] { nameof(Email) }));
        return Task.FromResult(ValidationResult.Success!);
    }
}
```

### External validation handler

```csharp
public class CreateUserValidator : IValidationHandler<CreateUser>
{
    private readonly IUserRepository _repo;
    public CreateUserValidator(IUserRepository repo) => _repo = repo;

    public async ValueTask<ValidationResult> ValidateAsync(CreateUser msg, CancellationToken ct)
    {
        if (await _repo.ExistsByEmailAsync(msg.Email, ct))
            return new ValidationResult("Email already registered", new[] { nameof(msg.Email) });
        return ValidationResult.Success!;
    }
}
```

### Catching validation errors

```csharp
try
{
    await mediator.Send(new CreateUser("", "John Doe"), ct);
}
catch (MessageValidationException ex)
{
    Console.WriteLine($"Validation failed: {ex.Message}");
}
```
