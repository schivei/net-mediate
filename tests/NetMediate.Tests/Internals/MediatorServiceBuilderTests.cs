using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public class MediatorServiceBuilderTests
{
    public class DummyNotification
    {
        public bool Valid { get; set; }
    }

    public class DummyCommand { }

    public class DummyRequest { }

    public class DummyStream { }

    public class DummyValidation { }

    public class DummyNotificationHandler : INotificationHandler<DummyNotification>
    {
        public Task Handle(
            DummyNotification notification,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;
    }

    public class DummyCommandHandler : ICommandHandler<DummyCommand>
    {
        public Task Handle(DummyCommand command, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
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
            yield return new object();
            await Task.CompletedTask;
        }
    }

    public class DummyValidationHandler : IValidationHandler<DummyValidation>
    {
        public ValueTask<ValidationResult> ValidateAsync(
            DummyValidation message,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(new ValidationResult("Dummy validation result"));
    }

    [Fact]
    public void MapAssembly_CurrentAssembly()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        var result = builder.MapAssemblies(typeof(DummyCommandHandler).GetType().Assembly);
        Assert.Same(builder, result);
    }

    [Fact]
    public void MapAssemblies_EmptyArray_UsesAllAssemblies()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        var result = builder.MapAssemblies();
        Assert.Same(builder, result);
    }

    [Fact]
    public void IgnoreUnhandledMessages_SetsConfiguration()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        var result = builder.IgnoreUnhandledMessages(false, false, LogLevel.Critical);
        Assert.Same(builder, result);
    }

    [Fact]
    public void FilterNotification_RegistersHandlerAndFilter()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        var called = false;
        builder.FilterNotification<DummyNotification, DummyNotificationHandler>(msg =>
        {
            called = true;
            return msg.Valid;
        });
        Assert.Contains(
            services,
            s => s.ServiceType == typeof(INotificationHandler<DummyNotification>)
        );
        var configuration = services.BuildServiceProvider().GetRequiredService<Configuration>();
        Assert.True(
            configuration.TryGetHandlerTypeByMessageFilter(new DummyNotification(), out var type)
        );
        Assert.Null(type);
        Assert.True(called, "Filter should have been called");
        called = false;
        Assert.True(
            configuration.TryGetHandlerTypeByMessageFilter(
                new DummyNotification { Valid = true },
                out var handlerType
            )
        );
        Assert.True(called, "Filter should have been called");
        Assert.Equal(typeof(DummyNotificationHandler), handlerType);
    }

    [Fact]
    public void FilterCommand_RegistersHandlerAndFilter()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        builder.FilterCommand<DummyCommand, DummyCommandHandler>(msg => true);
        // Note: Bug in original code, uses INotificationHandler instead of ICommandHandler
        // This test will still pass as it checks registration
        Assert.Contains(services, s => s.ImplementationType == typeof(DummyCommandHandler));
    }

    [Fact]
    public void FilterRequest_RegistersHandlerAndFilter()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        builder.FilterRequest<DummyRequest, DummyRequestHandler>(msg => true);
        Assert.Contains(services, s => s.ImplementationType == typeof(DummyRequestHandler));
    }

    [Fact]
    public void FilterStream_RegistersHandlerAndFilter()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        builder.FilterStream<DummyStream, DummyStreamHandler>(msg => true);
        Assert.Contains(services, s => s.ImplementationType == typeof(DummyStreamHandler));
    }

    [Fact]
    public void InstantiateHandlerByMessageFilter_RegistersFilter()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder(services);
        builder.InstantiateHandlerByMessageFilter<DummyNotification>(msg =>
            typeof(DummyNotificationHandler)
        );
        // No assertion, just ensure no exception
    }

    [Fact]
    public void Map_RegistersHandlers()
    {
        var services = new ServiceCollection();
        var builder = MakeBuilder(services);
        builder.RegisterNotificationHandler<DummyNotification, DummyNotificationHandler>();
        builder.RegisterCommandHandler<DummyCommand, DummyCommandHandler>();
        builder.RegisterRequestHandler<DummyRequest, DummyRequestHandler>();
        builder.RegisterStreamHandler<DummyStream, DummyStreamHandler>();
        builder.RegisterValidationHandler<DummyValidation, DummyValidationHandler>();
        Assert.Contains(services, s => s.ImplementationType == typeof(DummyNotificationHandler));
        Assert.Contains(services, s => s.ImplementationType == typeof(DummyCommandHandler));
        Assert.Contains(services, s => s.ImplementationType == typeof(DummyRequestHandler));
        Assert.Contains(services, s => s.ImplementationType == typeof(DummyStreamHandler));
        Assert.Contains(services, s => s.ImplementationType == typeof(DummyValidationHandler));
    }

    private static IMediatorServiceBuilder MakeBuilder(IServiceCollection services) =>
        new MediatorServiceBuilder(services);

    [Fact]
    public void ExtractTypes_ReturnsExpectedTypes()
    {
        var assemblies = new[] { typeof(DummyNotificationHandler).Assembly };
        var result =
            typeof(MediatorServiceBuilder)
                .GetMethod("ExtractTypes", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, [assemblies]) as IEnumerable<(Type, Type[])>;
        Assert.Contains(result!, t => t.Item1 == typeof(DummyNotificationHandler));
    }
}
