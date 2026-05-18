---
sidebar_position: 5
---

# Quartz

> **GenDI pattern:** The examples below assume `NetMediate.SourceGeneration` in the startup project. Prefer `[Injectable]` + `[Inject]` for serializers, notifiers, and supporting services.

`NetMediate.Quartz` is an optional package that decorates `IMediator` notification publishing with Quartz-backed persistence.

## Quick start

Configure Options in `appsettings.json`:

```json
{
    "QuartzNotificationOptions": {
        "GroupName": "MyApp",
        "MisfireRetryCount": 3
    }
}
```

Startup configuration:

```csharp
using NetMediate.Quartz;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configure Quartz first - AddNetMediateQuartz only registers Quartz decorators/jobs;
// it does not configure Quartz itself.
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// 2. Register NetMediate Quartz extensions
builder.Services.AddNetMediateQuartz();

// 3. Register NetMediate itself after Quartz extensions
builder.Services.AddNetMediate();
```

`AddNetMediateQuartz()` does **not** call `AddNetMediate()` for you. Call `AddNetMediateQuartz()` first, then `AddNetMediate()`, so the generated mediator setup can apply the Quartz mediator decorator.

## Serializer customization

```csharp
builder.Services.AddNetMediateQuartz();
builder.Services.AddNetMediate();
builder.Services.AddSingleton<INotificationSerializer, MyMessagePackSerializer>();
```
