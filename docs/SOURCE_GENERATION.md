# NetMediate.SourceGeneration

`NetMediate.SourceGeneration` is an optional Roslyn incremental source generator that emits handler registrations at compile time. It removes the need for manual `Register*Handler<>` calls at startup.

## Installation

Add the package as an **analyzer-only** reference (it must not be referenced as a normal library):

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x"
                  OutputItemType="Analyzer"
                  ReferenceOutputAssembly="false" />
```

## Usage

```csharp
using NetMediate;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddNetMediateGenerated();
```

The generator discovers all concrete (non-abstract, non-generic) classes that implement one of the NetMediate handler interfaces in your project:

| Discovered interface | Generated call |
|---|---|
| `ICommandHandler<TMessage>` | `configure.RegisterCommandHandler<THandler, TMessage>()` |
| `INotificationHandler<TMessage>` | `configure.RegisterNotificationHandler<THandler, TMessage>()` |
| `IRequestHandler<TMessage, TResponse>` | `configure.RegisterRequestHandler<THandler, TMessage, TResponse>()` |
| `IStreamHandler<TMessage, TResponse>` | `configure.RegisterStreamHandler<THandler, TMessage, TResponse>()` |

The generated method is decorated with `[ExcludeFromCodeCoverage]` — you do not need to test it directly.

If a class also implements `INotifiable` (e.g. a custom notifier), the generator uses `AddNetMediate<TNotifier>` instead of `AddNetMediate`.

## Manual no-scan fallback

You can register handlers explicitly without the source generator:

```csharp
builder.Services.AddNetMediate(configure =>
{
    configure.RegisterCommandHandler<CreateUserCommandHandler, CreateUserCommand>();
    configure.RegisterRequestHandler<GetUserRequestHandler, GetUserRequest, UserDto>();
    configure.RegisterNotificationHandler<UserCreatedNotificationHandler, UserCreatedNotification>();
    configure.RegisterStreamHandler<GetEventsQueryHandler, GetEventsQuery, EventDto>();
});
```

## AOT / NativeAOT

Both the source-generator path and the manual explicit path are fully AOT-safe — no reflection, no `MakeGenericType`, no assembly scanning. See [AOT.md](AOT.md) for the complete compatibility guide.
