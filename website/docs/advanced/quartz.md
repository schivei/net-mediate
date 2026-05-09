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

// 2. Register NetMediate itself after Quartz is available
builder.Services.AddNetMediate();

// 3. Replace the default notifier with the Quartz-based notifier
builder.Services.AddNetMediateQuartz(opts =>
{
    opts.GroupName = "MyApp";
});
```

`AddNetMediateQuartz()` does **not** call `AddNetMediate()` for you. It only replaces the `INotifiable` implementation used by notifications.

## Serializer customization

```csharp
builder.Services.AddNetMediate();
builder.Services.AddNetMediateQuartz();
builder.Services.AddSingleton<INotificationSerializer, MyMessagePackSerializer>();
```
