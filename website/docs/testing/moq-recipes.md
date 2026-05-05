---
sidebar_position: 1
---

# NetMediate.Moq Recipes

## Install

```xml
<PackageReference Include="NetMediate.Moq" Version="x.x.x" />
```

## Replace a service with a singleton mock

```csharp
var services = new ServiceCollection();
services.AddSingleton<IClock, SystemClock>();

var clockMock = services.AddMockSingleton<IClock>();
clockMock.Setup(x => x.UtcNow).Returns(DateTime.UnixEpoch);
```

## Create a mock without registering it

```csharp
var strict = Mocking.Strict<IMyService>();   // MockBehavior.Strict
var loose  = Mocking.Loose<IMyService>();    // MockBehavior.Loose
var def    = Mocking.Create<IMyService>();   // MockBehavior.Default
```

## Async setup helpers

```csharp
var mock = Mocking.Strict<IMyCommandHandler>();

// Returns Task.CompletedTask
mock.Setup(x => x.Execute()).ReturnsCompletedTask();

// Returns Task<TResult>
mock.Setup(x => x.Get()).ReturnsTaskResult("ok");

// Also available as ReturnsTask (alias)
mock.Setup(x => x.Get()).ReturnsTask("ok");
```

## Register a mediator mock quickly

```csharp
var services = new ServiceCollection();
var mediatorMock = services.AddMediatorMock();

mediatorMock
    .Setup(m => m.Send(It.IsAny<MyCommand>(), It.IsAny<CancellationToken>()))
    .ReturnsCompletedTask();
```

## Replace an existing registration with a mock

```csharp
// AddMockSingleton removes all existing registrations for the service type
// before adding the mock, so it is safe to call even if the service was already registered.
var mock = services.AddMockSingleton<IMyService>(MockBehavior.Strict);
```
