---
sidebar_position: 1
---

# Introduction to NetMediate

Welcome to NetMediate, a lightweight and efficient .NET implementation of the Mediator pattern for in-process messaging and communication between components.

## What is NetMediate?

NetMediate is a mediator pattern library for .NET that enables decoupled communication between components in your application. It provides a simple and flexible way to send commands, publish notifications, make requests, and handle streaming responses while maintaining clean architecture principles.

## Key Features

### 🚀 **Commands**
Send one-way messages to all registered handlers sequentially. Perfect for triggering side-effects across multiple consumers.

### 📨 **Notifications**
Publish messages to multiple handlers (fire-and-forget; per-handler errors are logged). Ideal for event-driven architectures.

### 🔄 **Requests**
Send a message to a single handler and receive a typed response. Great for queries and request-response patterns.

### 📡 **Streaming**
Handle requests that return multiple responses over time via `IAsyncEnumerable`. Perfect for real-time data feeds.

### 🔌 **Pipeline Behaviors**
Interceptors with pre/post flow for every message kind. Implement cross-cutting concerns like logging, validation, and caching.

### 🛡️ **Optional Resilience Package**
Retry, timeout, and circuit-breaker behaviors in `NetMediate.Resilience` to make your applications more robust.

### 📊 **OpenTelemetry-Ready Diagnostics**
Built-in `ActivitySource`/`Meter` for Send/Request/Notify/Stream operations with full distributed tracing support.

### 🐕 **Optional DataDog Integrations**
OpenTelemetry, Serilog, and ILogger support packages for comprehensive observability.

### 💉 **Dependency Injection**
Seamless integration with Microsoft.Extensions.DependencyInjection.

### ⏹️ **Cancellation Support**
Full cancellation token support across all operations.

### 🌐 **Broad Runtime Compatibility**
Multi-targeted for `net10.0`, `netstandard2.0`, and `netstandard2.1`.

## Why Use the Mediator Pattern?

The Mediator pattern helps you:

- **Decouple Components**: Reduce direct dependencies between classes
- **Single Responsibility**: Each handler focuses on one specific task
- **Testability**: Handlers can be easily unit tested in isolation
- **Maintainability**: Changes to one handler don't affect others
- **Flexibility**: Easy to add or remove handlers without affecting the rest of your code

## Architecture Overview

```
┌─────────────┐
│   Client    │
└──────┬──────┘
       │
       ├─── Send Command ────►┌──────────────┐      ┌─────────────────┐
       │                      │              │      │  Command        │
       ├─── Notify ──────────►│   IMediator  ├─────►│  Notification   │
       │                      │              │      │  Request        │
       ├─── Request ─────────►│              │      │  Stream         │
       │                      └──────────────┘      │  Handlers       │
       └─── RequestStream ───►                      └─────────────────┘
                                     │
                                     ▼
                              ┌─────────────────┐
                              │  Pipeline       │
                              │  Behaviors      │
                              └─────────────────┘
```

## Message Types

NetMediate supports four types of messages, each with a specific purpose:

| Message Kind | Handler Interface | Dispatch Semantics |
|--------------|-------------------|-------------------|
| **Command** | `ICommandHandler<TMessage>` | All registered handlers, sequential in registration order |
| **Request** | `IRequestHandler<TMessage, TResponse>` | First registered handler only; returns `TResponse` |
| **Notification** | `INotificationHandler<TMessage>` | All registered handlers, individual fire-and-forget per handler |
| **Stream** | `IStreamHandler<TMessage, TResponse>` | Single registered handler; yields items asynchronously |

## No Marker Interfaces Required

Unlike some mediator implementations, NetMediate **does not require marker interfaces** on your message types. Any plain class or record can be a message:

```csharp
public record CreateUserCommand(string Email, string Name);  // ✅ Works!
public record UserCreatedNotification(string UserId);        // ✅ Works!
public record GetUserQuery(string UserId);                   // ✅ Works!
```

## Getting Started

Ready to dive in? Head over to the [Installation Guide](./getting-started/installation.md) to get NetMediate set up in your project, or jump straight to the [Quick Start](./getting-started/quick-start.md) for a working example.

## Package Ecosystem

NetMediate consists of several packages:

- **NetMediate** - Core mediator implementation
- **NetMediate.SourceGeneration** - Compile-time handler registration (AOT-safe)
- **NetMediate.Resilience** - Retry, timeout, and circuit breaker behaviors
- **NetMediate.Diagnostics** - OpenTelemetry integration
- **NetMediate.Quartz** - Persistent notifications with Quartz.NET
- **NetMediate.Moq** - Testing utilities for Moq
- **NetMediate.DataDog.OpenTelemetry** - DataDog OTLP exporter integration
- **NetMediate.DataDog.Serilog** - DataDog Serilog sink integration
- **NetMediate.DataDog.ILogger** - DataDog ILogger integration

## Community and Support

- **GitHub**: [schivei/net-mediate](https://github.com/schivei/net-mediate)
- **Issues**: [Report bugs or request features](https://github.com/schivei/net-mediate/issues)
- **Discussions**: [Ask questions and share ideas](https://github.com/schivei/net-mediate/discussions)
- **NuGet**: [NetMediate packages](https://www.nuget.org/packages/NetMediate/)

## License

NetMediate is licensed under the MIT License. See the [LICENSE](https://github.com/schivei/net-mediate/blob/main/LICENSE) file for details.
