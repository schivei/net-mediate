using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using NetMediate;
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

    public class DummyValidationHandler : IValidationHandler<DummyValidation>
    {
        public Task<ValidationResult> ValidateAsync(
            DummyValidation message,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(new ValidationResult("Dummy validation result"));
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
}
