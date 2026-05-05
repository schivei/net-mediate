---
sidebar_position: 1
---

# NetMediate.SourceGeneration

`NetMediate.SourceGeneration` is a Roslyn incremental source generator that emits handler registrations automatically at compile time. It is the standard and only supported registration path for NetMediate handlers.

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
builder.Services.AddNetMediate();
```

That's it. The generator discovers all concrete (non-abstract, non-generic) classes that implement one of the NetMediate handler interfaces in your project and wires them up:

| Discovered interface | Generated call |
|---|---|
| `ICommandHandler<TMessage>` | `configure.RegisterCommandHandler<THandler, TMessage>()` |
| `INotificationHandler<TMessage>` | `configure.RegisterNotificationHandler<THandler, TMessage>()` |
| `IRequestHandler<TMessage, TResponse>` | `configure.RegisterRequestHandler<THandler, TMessage, TResponse>()` |
| `IStreamHandler<TMessage, TResponse>` | `configure.RegisterStreamHandler<THandler, TMessage, TResponse>()` |

The generated method is decorated with `[ExcludeFromCodeCoverage]` — you do not need to test it directly.

If a class also implements `INotifiable` (e.g. a custom notifier), the generator uses `UseNetMediate<TNotifier>` instead of `UseNetMediate`.

## AOT / NativeAOT

The source-generator path is fully AOT-safe — no reflection, no `MakeGenericType`, no assembly scanning. See [AOT.md](AOT.md) for the complete compatibility guide.
