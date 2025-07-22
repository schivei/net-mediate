using Microsoft.Extensions.DependencyInjection;
using NetMediate.Internals.Workers;
using System.Diagnostics.CodeAnalysis;

[assembly: ExcludeFromCodeCoverage]

namespace NetMediate.Tests;

internal sealed class NetMediateFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; private set; }
    public CancellationTokenSource CancellationTokenSource { get; } = new CancellationTokenSource();

    public NetMediateFixture()
    {
        var services = new ServiceCollection();
        services.AddNetMediate();

        ServiceProvider = services.BuildServiceProvider();

        RunHostedServices();
    }

    private void RunHostedServices()
    {
        var notificationWorker = ServiceProvider.GetRequiredService<NotificationWorker>();

        notificationWorker.StartAsync(CancellationTokenSource.Token);
    }

    public void Dispose()
    {
        CancellationTokenSource.Cancel(false);
        CancellationTokenSource.Dispose();
        if (ServiceProvider is IDisposable disposable)
            disposable.Dispose();

        ServiceProvider = null!;
    }
}
