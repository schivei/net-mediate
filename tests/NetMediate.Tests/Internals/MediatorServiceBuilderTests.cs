using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals;

using Notifier = NetMediate.Moq.Notifier;

namespace NetMediate.Tests.Internals;

public class MediatorServiceBuilderTests
{
    public class DummyNotification : INotification
    {
        public bool Valid { get; set; }
    }

    public class DummyCommand : ICommand { }

    public class DummyRequest : IRequest<object> { }

    public class DummyStream : IStream<object> { }

    public class DummyValidation : IMessage { }

    public class DummyNotificationHandler : INotificationHandler<DummyNotification>
    {
        public ValueTask Handle(
            DummyNotification notification,
            CancellationToken cancellationToken = default
        ) => ValueTask.CompletedTask;
    }

    public class DummyCommandHandler : ICommandHandler<DummyCommand>
    {
        public ValueTask Handle(DummyCommand command, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    public class DummyRequestHandler : IRequestHandler<DummyRequest, object>
    {
        public ValueTask<object> Handle(
            DummyRequest query,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult<object>(null!);
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
        var builder = new MediatorServiceBuilder<Notifier>(services);
        var result = builder.MapAssemblies(typeof(DummyCommandHandler).GetType().Assembly);
        Assert.Same(builder, result);
    }

    [Fact]
    public void MapAssemblies_EmptyArray_UsesAllAssemblies()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);
        var result = builder.MapAssemblies();
        Assert.Same(builder, result);
    }

    [Fact]
    public void IgnoreUnhandledMessages_SetsConfiguration()
    {
        var services = new ServiceCollection();
        var builder = new MediatorServiceBuilder<Notifier>(services);
        var result = builder.IgnoreUnhandledMessages(false);
        Assert.Same(builder, result);
    }
}
