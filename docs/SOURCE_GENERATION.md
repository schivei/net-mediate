# NetMediate.SourceGeneration

`NetMediate.SourceGeneration` is an optional package that generates handler registrations at compile time.

This reduces startup reflection by replacing assembly scanning with generated registration code.

## Installation

```xml
<PackageReference Include="NetMediate.SourceGeneration" Version="x.x.x" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Usage

```csharp
using NetMediate;

var builder = Host.CreateApplicationBuilder();
builder.Services.AddNetMediateGenerated();
```

The generator emits `AddNetMediateGenerated(...)`, which calls explicit registrations for discovered handlers.

## Manual no-scan fallback

You can register handlers explicitly (without source generator) using:

```csharp
builder.Services.AddNetMediate(registration =>
{
    registration.RegisterCommandHandler<CreateUserCommand, CreateUserCommandHandler>();
    registration.RegisterRequestHandler<GetUserRequest, UserDto, GetUserRequestHandler>();
});
```
