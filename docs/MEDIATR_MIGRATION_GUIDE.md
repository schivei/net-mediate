# MediatR Migration Guide (NetMediate.Compat)

This guide covers migration from MediatR contracts to NetMediate with minimal friction using `NetMediate.Compat`.

## 1) Install package

```xml
<PackageReference Include="NetMediate.Compat" Version="x.x.x" />
```

## 2) Keep MediatR contracts unchanged

```csharp
using MediatR;

public sealed record CreateOrder(string Id) : IRequest<OrderCreated>;
public sealed record OrderCreated(string Id);

public sealed class CreateOrderHandler : IRequestHandler<CreateOrder, OrderCreated>
{
    public Task<OrderCreated> Handle(CreateOrder request, CancellationToken cancellationToken)
        => Task.FromResult(new OrderCreated(request.Id));
}
```

## 3) Replace DI registration only

```csharp
using MediatR;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<CreateOrderHandler>());

var provider = services.BuildServiceProvider();
var mediator = provider.GetRequiredService<IMediator>();

var result = await mediator.Send(new CreateOrder("123"));
```

## 4) Advanced registration options

```csharp
services.AddMediatR(typeof(CreateOrderHandler).Assembly);
```

## 5) Runtime object overloads

`IMediator.Send(object)`, `IMediator.Publish(object)`, and `IMediator.CreateStream(object)` are supported through the compat adapter.

## 6) Validation checklist

- [x] `IMediator`, `ISender`, and `IPublisher` resolve from DI
- [x] Request/command/notification/stream dispatch works with MediatR contracts
- [x] Object overloads route correctly by runtime type
- [x] Covered by tests in `tests/SharedParity.MediatRCompat.Tests`
