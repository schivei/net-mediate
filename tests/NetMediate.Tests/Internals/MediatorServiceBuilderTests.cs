using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;
using NetMediate.Internals;

using Notifier = NetMediate.Moq.Notifier;

namespace NetMediate.Tests.Internals;

public class MediatorServiceBuilderTests
{
    public class DummyNotification { public bool Valid { get; set; } }

    public class DummyCommand { }

    public class DummyRequest { }

    public class DummyStream { }

    public class DummyValidation : IMessage { }

    public class DummyNotificationHandler : INotificationHandler<DummyNotification>
    {
        public Task Handle(
            DummyNotification notification,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    public class DummyCommandHandler : ICommandHandler<DummyCommand>
    {
        public Task Handle(DummyCommand command, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    public class DummyRequestHandler : IRequestHandler<DummyRequest, object>
    {
        public Task<object> Handle(
            DummyRequest query,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<object>(null!);
    }

    public class DummyStreamHandler : IStreamHandler<DummyStream, object>
    {
        public async IAsyncEnumerable<object> Handle(
            DummyStream query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            yield return new();
            await Task.CompletedTask;
        }
    }

    [Fact]
    public void Constructor_WhenINotifiableAlreadyRegistered_ReplacesExistingRegistration()
    {
        // Arrange — pre-register a different INotifiable implementation
        var services = new ServiceCollection();
        services.AddSingleton<INotifiable, Notifier>(sp => null!); // placeholder

        // Act — constructor should Replace rather than Add
        var builder = new MediatorServiceBuilder<Notifier>(services);

        // Assert — only one INotifiable is registered and it is Notifier
        var registrations = services.Where(s => s.ServiceType == typeof(INotifiable)).ToList();
        Assert.Single(registrations);
        Assert.Equal(typeof(Notifier), registrations[0].ImplementationType);
    }

    [Fact]
    public void Guard_ThrowIfNull_WithNull_ThrowsArgumentNullException()
    {
        // Guard.ThrowIfNull must throw when the argument is null
        object? value = null;
        Assert.Throws<ArgumentNullException>(() => Guard.ThrowIfNull(value));
    }

    [Fact]
    public void Guard_ThrowIfNull_WithNonNull_DoesNotThrow()
    {
        // Guard.ThrowIfNull must not throw for a non-null argument
        object value = new();
        Guard.ThrowIfNull(value); // no exception
    }

    [Fact]
    public void RegisterHandler_AddsHandlerToServiceCollection()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);

        builder.RegisterHandler<INotificationHandler<DummyNotification>, DummyNotificationHandler, DummyNotification, Task>();

        Assert.Contains(
            services,
            s => s.ServiceType == typeof(INotificationHandler<DummyNotification>)
                 && s.ImplementationType == typeof(DummyNotificationHandler)
        );
    }

    [Fact]
    public void RegisterBehavior_AddsBehaviorToServiceCollection()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);
        builder.RegisterBehavior<NoOpBehavior<DummyNotification>, DummyNotification, Task>();

        Assert.Contains(
            services,
            s => s.ServiceType == typeof(IPipelineBehavior<DummyNotification, Task>)
                 && s.ImplementationType == typeof(NoOpBehavior<DummyNotification>)
        );
    }

    // ── Specialized type-based registration (AOT-safe) ──────────────────────────

    [Fact]
    public void RegisterCommandHandler_RegistersHandlerAndExecutor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);

        builder.RegisterCommandHandler<DummyCommandHandler, DummyCommand>();

        Assert.Contains(services, s => s.ServiceType == typeof(ICommandHandler<DummyCommand>)
            && s.ImplementationType == typeof(DummyCommandHandler));
        Assert.Contains(services, s =>
            s.ServiceType == typeof(PipelineExecutor<DummyCommand, Task, ICommandHandler<DummyCommand>>));
    }

    [Fact]
    public void RegisterNotificationHandler_RegistersHandlerAndExecutor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);

        builder.RegisterNotificationHandler<DummyNotificationHandler, DummyNotification>();

        Assert.Contains(services, s => s.ServiceType == typeof(INotificationHandler<DummyNotification>)
            && s.ImplementationType == typeof(DummyNotificationHandler));
        Assert.Contains(services, s =>
            s.ServiceType == typeof(NotificationPipelineExecutor<DummyNotification>));
    }

    [Fact]
    public void RegisterRequestHandler_RegistersHandlerAndExecutor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);

        builder.RegisterRequestHandler<DummyRequestHandler, DummyRequest, object>();

        Assert.Contains(services, s => s.ServiceType == typeof(IRequestHandler<DummyRequest, object>)
            && s.ImplementationType == typeof(DummyRequestHandler));
        Assert.Contains(services, s =>
            s.ServiceType == typeof(RequestPipelineExecutor<DummyRequest, object>));
    }

    [Fact]
    public void RegisterStreamHandler_RegistersHandlerAndExecutor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);

        builder.RegisterStreamHandler<DummyStreamHandler, DummyStream, object>();

        Assert.Contains(services, s => s.ServiceType == typeof(IStreamHandler<DummyStream, object>)
            && s.ImplementationType == typeof(DummyStreamHandler));
        Assert.Contains(services, s =>
            s.ServiceType == typeof(StreamPipelineExecutor<DummyStream, object>));
    }

    // ── Instance-based registration ──────────────────────────────────────────────

    [Fact]
    public void RegisterCommandHandler_Instance_RegistersHandlerAndExecutor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);
        var handler = new DummyCommandHandler();

        builder.RegisterCommandHandler<DummyCommand>(handler);

        Assert.Contains(services, s => s.ServiceType == typeof(ICommandHandler<DummyCommand>)
            && s.ImplementationInstance == handler);
        Assert.Contains(services, s =>
            s.ServiceType == typeof(PipelineExecutor<DummyCommand, Task, ICommandHandler<DummyCommand>>));
    }

    [Fact]
    public void RegisterNotificationHandler_Instance_RegistersHandlerAndExecutor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);
        var handler = new DummyNotificationHandler();

        builder.RegisterNotificationHandler<DummyNotification>(handler);

        Assert.Contains(services, s => s.ServiceType == typeof(INotificationHandler<DummyNotification>)
            && s.ImplementationInstance == handler);
        Assert.Contains(services, s =>
            s.ServiceType == typeof(NotificationPipelineExecutor<DummyNotification>));
    }

    [Fact]
    public void RegisterRequestHandler_Instance_RegistersHandlerAndExecutor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);
        var handler = new DummyRequestHandler();

        builder.RegisterRequestHandler<DummyRequest, object>(handler);

        Assert.Contains(services, s => s.ServiceType == typeof(IRequestHandler<DummyRequest, object>)
            && s.ImplementationInstance == handler);
        Assert.Contains(services, s =>
            s.ServiceType == typeof(RequestPipelineExecutor<DummyRequest, object>));
    }

    [Fact]
    public void RegisterStreamHandler_Instance_RegistersHandlerAndExecutor()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);
        var handler = new DummyStreamHandler();

        builder.RegisterStreamHandler<DummyStream, object>(handler);

        Assert.Contains(services, s => s.ServiceType == typeof(IStreamHandler<DummyStream, object>)
            && s.ImplementationInstance == handler);
        Assert.Contains(services, s =>
            s.ServiceType == typeof(StreamPipelineExecutor<DummyStream, object>));
    }

    private sealed class NoOpBehavior<TMessage> : IPipelineBehavior<TMessage, Task>
        where TMessage : notnull
    {
        public Task Handle(TMessage message, PipelineBehaviorDelegate<TMessage, Task> next, CancellationToken ct = default) =>
            next(message, ct);
    }
}

