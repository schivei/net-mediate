# NetMediate.Moq Recipes

## Install

```xml
<PackageReference Include="NetMediate.Moq" Version="x.x.x" />
```

## Replace service with singleton mock

```csharp
var services = new ServiceCollection();
services.AddSingleton<IClock, SystemClock>();

var clockMock = services.AddMockSingleton<IClock>();
clockMock.Setup(x => x.UtcNow).Returns(DateTime.UnixEpoch);
```

## Strict and Loose helper factories

```csharp
var strict = Mocking.Strict<IMyService>();
var loose = Mocking.Loose<IMyService>();
```

## Async setup helpers

```csharp
var mock = Mocking.Strict<IAsyncSample>();
mock.Setup(x => x.Execute()).ReturnsCompletedTask();
mock.Setup(x => x.GetValue()).ReturnsValueTask("ok");
```

## Register mediator mock quickly

```csharp
var services = new ServiceCollection();
var mediatorMock = services.AddMediatorMock();
```

## Validation checklist

- [x] Singleton replacement and mock resolution
- [x] Strict/loose helper usage
- [x] Async fluent setup helpers
- [x] Covered by tests in `tests/NetMediate.Tests/MoqSupport/NetMediateMoqTests.cs`
