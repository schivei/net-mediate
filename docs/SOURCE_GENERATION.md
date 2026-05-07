# NetMediate.SourceGeneration

`NetMediate.SourceGeneration` is a Roslyn incremental source generator that emits handler registrations automatically at compile time. It is the standard and only supported registration path for NetMediate handlers.

The source generator is **bundled inside the `NetMediate` package** — you do not need to install `NetMediate.SourceGeneration` separately. It is loaded automatically for any project that directly references `NetMediate`. The `GenDI.SourceGenerator` is also bundled so you can annotate your own classes with `[Injectable]` etc. without installing GenDI separately.

## Installation

```xml
<PackageReference Include="NetMediate" Version="x.x.x" />
```

That is all. `dotnet add package NetMediate` also works without any extra configuration — the bundled analyzers are loaded automatically by MSBuild for any project that directly references the package.

> **Library projects:** Add `PrivateAssets="all"` to prevent `NetMediate` and its bundled analyzers from flowing as a transitive dependency to downstream consumers of your library. This does **not** affect whether the analyzers run for your own project — they always run for direct references.

```xml
<!-- Library project recommendation -->
<PackageReference Include="NetMediate" Version="x.x.x" PrivateAssets="all" />
```

### Bundled analyzers

The `NetMediate` package ships two source generators under `analyzers/dotnet/cs/`:

| Generator DLL | What it generates |
|---|---|
| `NetMediate.SourceGeneration.dll` | `AddNetMediate()`, `NetMediateGeneratedDI`, `NetMediateTypedExtensions`, global usings |
| `GenDI.SourceGenerator.dll` | `AddGenDIServices()` for your own `[Injectable]`-annotated classes |

Because `GenDI.SourceGenerator.dll` is bundled, you can use `[Injectable]`, `[ServiceInjection]`, and related attributes **without installing a separate package**.

Both generators also propagate transitively via a `buildTransitive/NetMediate.props` file. This means that if a library in your solution references NetMediate (without `PrivateAssets="all"`), the generators will also run in projects that consume that library — no extra package reference required.

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

> **Keyed handlers**: The source generator handles two cases automatically:
> - Handler decorated with `[KeyedService(Key = "mykey")]` → registered with the explicit key `"mykey"`.
> - Handler with no attribute → registered under `Extensions.DEFAULT_ROUTING_KEY = "__default"` (the same key used when `null` is passed at dispatch time, so `mediator.Send(command, ct)` and `mediator.Send(null, command, ct)` are equivalent).
>
> If you want to register a handler under a custom key *without* using the `[KeyedService]` attribute, you must register it manually via `UseNetMediate`. Avoid using the reserved literal `"__default"` as your own routing key.

## AOT / NativeAOT

The source-generator path is fully AOT-safe — no reflection, no `MakeGenericType`, no assembly scanning. See [AOT.md](AOT.md) for the complete compatibility guide.

## Controlling registration order with `[ServiceOrder]`

Apply `[ServiceOrder(n)]` to a handler class to control the order in which it is registered by
the source generator. Lower values are registered first.

```csharp
[ServiceOrder(1)]
public sealed class AuditHandler : ICommandHandler<AuditCommand> { ... }

[ServiceOrder(2)]
public sealed class MetricsHandler : ICommandHandler<MetricsCommand> { ... }

// No attribute → registered last (implicit order = int.MaxValue).
public sealed class FallbackHandler : ICommandHandler<FallbackCommand> { ... }
```

Registration order affects the **pipeline wrapping order**: behaviors registered earlier wrap
the pipeline *outermost*, so they run before later-registered behaviors.

> **Scope**: `[ServiceOrder]` is respected only by the source generator. Handlers registered
> manually via `UseNetMediate(configure => ...)` follow the order you write them in code.

## Generated namespace and `AddNetMediate()` discoverability

The generator places `NetMediateGeneratedDI` (and its `AddNetMediate()` extension method) in a
namespace derived from your project's root namespace:

```
<YourRootNamespace>.NetMediate
```

For C# 10 and later the generator also emits a companion `NetMediateGlobalUsings.g.cs` file that
adds `global using <YourRootNamespace>.NetMediate;` to the project automatically. This means
`AddNetMediate()` is available everywhere in your project without any manual `using` directive.

If your project targets C# 9 or earlier, add the using directive explicitly in your entry-point
file:

```csharp
// Program.cs or Startup.cs
using MyApp.NetMediate;          // the generated namespace

builder.Services.AddNetMediate();
```

### Namespace selection algorithm

The generator uses the **current project's assembly name** directly — one namespace per project, resolved independently, matching the same per-project strategy used by GenDI. For example:

| Assembly name | Generated namespace |
|---|---|
| `Acme.Web` | `Acme.Web.NetMediate` |
| `Acme.Api` | `Acme.Api.NetMediate` |
| `MyApp` | `MyApp.NetMediate` |

Each project always gets its own isolated namespace. No cross-project or cross-build state is involved.

Projects in the `NetMediate.*` name space are skipped automatically (unless they are test or benchmark assemblies).

## Typed dispatch extension methods

Starting with this release, the source generator also emits a second file —
`NetMediateTypedExtensions.g.cs` — that contains **named, fully-typed extension methods** for
every message type it discovers in your project. These methods are AOT-safe and reflection-free:
they call the concrete `IMediator` overloads directly with both type arguments resolved at
compile time.

### Generated method names

| Handler interface | Verb | Example generated method |
|---|---|---|
| `ICommandHandler<MyCmd>` | `Send` | `SendMyCmdAsync(...)` |
| `INotificationHandler<MyEvt>` | `Notify` | `NotifyMyEvtAsync(...)` |
| `IRequestHandler<MyQuery, MyResponse>` | `Request` | `RequestMyQueryAsync(...)` |
| `IStreamHandler<MyFeed, MyItem>` | `Stream` | `StreamMyFeedAsync(...)` |

### Overloads generated per message type

**Commands and notifications** receive four overloads:

```csharp
// 1. Key-less dispatch (uses the default routing key)
Task SendMyCmdAsync(this IMediator mediator, MyCmd message, CancellationToken ct = default);

// 2. Explicit routing key
Task SendMyCmdAsync(this IMediator mediator, object? key, MyCmd message, CancellationToken ct = default);

// 3. Batch dispatch (key-less)
Task SendMyCmdAsync(this IMediator mediator, IEnumerable<MyCmd> messages, CancellationToken ct = default);

// 4. Batch dispatch with explicit key
Task SendMyCmdAsync(this IMediator mediator, object? key, IEnumerable<MyCmd> messages, CancellationToken ct = default);
```

**Requests** receive two overloads:

```csharp
Task<MyResponse> RequestMyQueryAsync(this IMediator mediator, MyQuery message, CancellationToken ct = default);
Task<MyResponse> RequestMyQueryAsync(this IMediator mediator, object? key, MyQuery message, CancellationToken ct = default);
```

**Streams** receive two overloads:

```csharp
IAsyncEnumerable<MyItem> StreamMyFeedAsync(this IMediator mediator, MyFeed message, CancellationToken ct = default);
IAsyncEnumerable<MyItem> StreamMyFeedAsync(this IMediator mediator, object? key, MyFeed message, CancellationToken ct = default);
```

### Usage example

With the global using emitted by the generator (C# 10+) you can write:

```csharp
// Instead of: mediator.Request<MyQuery, MyResponse>(new MyQuery(id), ct)
var result = await mediator.RequestMyQueryAsync(new MyQuery(id), ct);

// Batch send:
await mediator.SendMyCmdAsync(commands, ct);

// Keyed dispatch:
var value = await mediator.RequestMyQueryAsync("tenant-a", new MyQuery(id), ct);
```

### Conflict resolution

If two different message types share the same simple name (e.g., `Commands.PingCommand` and
`Events.PingCommand`), the generator **disambiguates** by deriving the method name from the
fully-qualified type name with dots removed:

```csharp
// Instead of the conflicting SendPingCommandAsync:
Task SendCommandsPingCommandAsync(...);
Task SendEventsPingCommandAsync(...);
```

### Deduplication

Multiple handlers for the **same** message type (e.g., one default and one keyed handler) produce
only **one** set of extension methods — the keyed handler is reached via the `object? key`
overload.

