---
sidebar_position: 5
---

# Quartz

> **GenDI pattern:** The examples below assume `NetMediate.SourceGeneration` in the startup project. Prefer `[Injectable]` + `[Inject]` for serializers, notifiers, and supporting services.

`NetMediate.Quartz` is an optional package that swaps the default NetMediate notification transport for Quartz-backed persistence.

## Quick start

```csharp
using NetMediate.Quartz;
using Quartz;

var builder = Host.CreateApplicationBuilder(args);

// 1. Configure Quartz first - AddNetMediateQuartz only swaps the notifier implementation;
// it does not configure Quartz itself.
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// 2. Register the Quartz notifier first so AddNetMediate picks up that INotifiable implementation
builder.Services.AddNetMediateQuartz(opts =>
{
    opts.GroupName = "MyApp";
});

// 3. Register NetMediate itself after the Quartz notifier is in place
builder.Services.AddNetMediate();
```

`AddNetMediateQuartz()` does **not** call `AddNetMediate()` for you. Call `AddNetMediateQuartz()` first, then `AddNetMediate()`, so the generated mediator setup uses the Quartz-backed `INotifiable` implementation.

## Serializer customization

```csharp
builder.Services.AddNetMediateQuartz();
builder.Services.AddNetMediate();
builder.Services.AddSingleton<INotificationSerializer, MyMessagePackSerializer>();
```
