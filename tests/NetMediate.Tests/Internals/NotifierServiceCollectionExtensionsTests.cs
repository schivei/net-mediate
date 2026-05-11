using Microsoft.Extensions.DependencyInjection;
using NetMediate.DependencyInjection;
using NetMediate.Internals;

namespace NetMediate.Tests.Internals;

public sealed class NotifierServiceCollectionExtensionsTests
{
    [Fact]
    public void TryAddDefaultNetMediateNotifier_WhenMissing_RegistersNotifier()
    {
        var services = new ServiceCollection();

        var returned = services.TryAddDefaultNetMediateNotifier();

        Assert.Same(services, returned);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<Notifier>(provider.GetRequiredService<INotifiable>());
    }

    [Fact]
    public void TryAddDefaultNetMediateNotifier_WhenAlreadyRegistered_PreservesExistingImplementation()
    {
        var services = new ServiceCollection();
        var existing = new ExistingNotifiable();
        services.AddSingleton<INotifiable>(existing);

        services.TryAddDefaultNetMediateNotifier();

        using var provider = services.BuildServiceProvider();
        Assert.Same(existing, provider.GetRequiredService<INotifiable>());
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(INotifiable));
    }

    private sealed class ExistingNotifiable : INotifiable
    {
        public Task DispatchNotifications<TMessage>(
            object? key,
            TMessage message,
            INotificationHandler<TMessage>[] handlers,
            CancellationToken cancellationToken = default)
            where TMessage : notnull => Task.CompletedTask;

        public Task Notify<TMessage>(
            object? key,
            TMessage message,
            CancellationToken cancellationToken = default)
            where TMessage : notnull => Task.CompletedTask;

        public Task Notify<TMessage>(
            object? key,
            IEnumerable<TMessage> messages,
            CancellationToken cancellationToken = default)
            where TMessage : notnull => Task.CompletedTask;
    }
}
