# NetMediate.Quartz

> **GenDI pattern:** The examples below assume `NetMediate.SourceGeneration` in the startup project. Prefer `[Injectable]` + `[Inject]` for serializers, notifiers, and supporting services.

`NetMediate.Quartz` is an optional package that decorates `IMediator` notification publishing with Quartz-backed persistence.

## Why Quartz for notifications?

The default NetMediate notification path dispatches handlers immediately in-process. `NetMediate.Quartz` persists notifications as Quartz jobs so they can survive process restarts and run across clustered nodes.

> This integration affects **only notifications**. Commands, requests, and streams continue to use the normal NetMediate pipeline.

## Installation

```bash
dotnet add package NetMediate.Quartz
dotnet add package Quartz
dotnet add package Quartz.Extensions.DependencyInjection
dotnet add package Quartz.Extensions.Hosting
```

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
builder.Services.AddNetMediateQuartz(); // it's also imports the NetMediate auto-registration source generator.

```

`AddNetMediateQuartz()` already calls `AddNetMediate()` for you.

## Configuration

`AddNetMediateQuartz` accepts an optional `QuartzNotificationOptions` via appsettings:

| Property | Default | Description |
|---|---|---|
| `GroupName` | `"NetMediate"` | Quartz group name for all notification jobs. |
| `MisfireRetryCount` | `1` | How many times Quartz will retry a misfired job. |

```json
{
    "QuartzNotificationOptions": {
        "GroupName": "Notifications",
        "MisfireRetryCount": 3
    }
}
```

## Customizing serialization

By default messages are serialized with `System.Text.Json`. You can replace the serializer after `AddNetMediateQuartz`:

```csharp
// must be registered before AddNetMediateQuartz, otherwise the default serializer will be used.
builder.Services.AddSingleton<INotificationSerializer, MyMessagePackSerializer>();

builder.Services.AddNetMediateQuartz();
```

## Persistent job store (recommended for production)

For crash recovery, configure Quartz with a persistent store before calling `AddNetMediateQuartz`:

```csharp
builder.Services.AddQuartz(q =>
{
    q.UseMicrosoftDependencyInjectionJobFactory();
    q.UsePersistentStore(store =>
    {
        store.UseProperties = true;
        store.UseSqlServer("Server=...;Database=Quartz;...");
        store.UseJsonSerializer();
    });
});
```

## Cluster mode

Enable Quartz clustering to distribute notification execution across nodes:

```csharp
builder.Services.AddQuartz(q =>
{
    q.SchedulerName = "NetMediateCluster";
    q.SchedulerId = "AUTO";

    q.UsePersistentStore(store =>
    {
        store.UseProperties = true;
        store.UseSqlServer("...");
        store.UseJsonSerializer();
        store.UseClustering();
    });
});
```
