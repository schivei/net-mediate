---
sidebar_position: 2
---

# Requests

Requests follow the request-response pattern, returning a typed result from a single handler.

For detailed request documentation, see the main [README](https://github.com/schivei/net-mediate#requests).

## Complete Example

The following example implements a product catalogue query. The handler reads from a read-model repository and returns a rich DTO.

```csharp
using GenDI;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;

// ----- Messages -----
public record GetOrderDetailsQuery(string OrderId);

public record OrderDetailsDto(
    string OrderId,
    string Status,
    decimal Total,
    IReadOnlyList<OrderLineDto> Lines);

public record OrderLineDto(string ProductName, int Quantity, decimal UnitPrice);

// ----- Shared service -----
[ServiceInjection]
public interface IOrderReadRepository
{
    Task<OrderDetailsDto?> GetByIdAsync(string orderId, CancellationToken ct);
}

// ----- Handler -----
[Injectable(ServiceLifetime.Scoped, Group = 100, Order = 1)]
public class GetOrderDetailsHandler : IRequestHandler<GetOrderDetailsQuery, OrderDetailsDto>
{
    [Inject] public required IOrderReadRepository Repository { get; init; }

    public async Task<OrderDetailsDto> Handle(GetOrderDetailsQuery query, CancellationToken ct)
    {
        var order = await Repository.GetByIdAsync(query.OrderId, ct);

        if (order is null)
            throw new KeyNotFoundException($"Order '{query.OrderId}' not found.");

        return order;
    }
}

// ----- Usage (Minimal API) -----
app.MapGet("/orders/{id}", async (string id, IMediator mediator, CancellationToken ct) =>
{
    try
    {
        var details = await mediator.RequestGetOrderDetailsQueryAsync(
            new GetOrderDetailsQuery(id), ct);
        return Results.Ok(details);
    }
    catch (KeyNotFoundException ex)
    {
        return Results.NotFound(new { ex.Message });
    }
});
```

Only one handler is invoked. If you register a second `IRequestHandler<GetOrderDetailsQuery, OrderDetailsDto>`, only the first registered one is called.


