using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NetMediate.Internals;

[assembly: ExcludeFromCodeCoverage]

namespace NetMediate.Tests;

public sealed class NetMediateFixture : IDisposable
{
    private IHost? _app;
    private readonly HostApplicationBuilder _builder;
    public Exception? RunError { get; private set; }

    public CancellationTokenSource CancellationTokenSource { get; } = new();

    public NetMediateFixture()
    {
        _builder = Host.CreateApplicationBuilder();
        _builder.Services.AddSingleton(this);
        var builder = new MediatorServiceBuilder(_builder.Services);
        builder.MapAssemblies(GetType().Assembly);
        builder.MapAssemblies();
    }

    public async Task RunAsync(Func<IServiceProvider, Task> runner)
    {
        _builder.Services.AddSingleton(new Runner(runner));
        _builder.Services.AddSingleton(
            new Terminator(async () =>
            {
                try
                {
                    await (_app?.StopAsync(CancellationTokenSource.Token) ?? Task.CompletedTask);
                    CancellationTokenSource.Cancel(false);
                }
                catch
                {
                    // ignore exceptions during shutdown
                }
            })
        );

        _builder.Services.AddHostedService<RunnerBackgroundService>();

        _app = _builder.Build();

        await _app.StartAsync(CancellationTokenSource.Token);
    }

    public async Task<TResponse> RunAsync<TResponse>(Func<IServiceProvider, Task<TResponse>> runner)
    {
        TResponse? response = default;
        await RunAsync(
            async (sp) =>
            {
                response = await runner(sp);
            }
        );
        await WaitAsync();

        return response!;
    }

    public Task WaitAsync() =>
        _app?.WaitForShutdownAsync(CancellationTokenSource.Token) ?? Task.CompletedTask;

    public void Dispose()
    {
        _app?.StopAsync(CancellationTokenSource.Token).GetAwaiter().GetResult();
        CancellationTokenSource.Cancel(false);
        CancellationTokenSource.Dispose();
        _app?.Dispose();
    }

    private class RunnerBackgroundService(
        IServiceProvider ServiceProvider,
        Runner runner,
        Terminator terminator
    ) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = ServiceProvider.CreateScope();

                await runner.RunAsync(scope.ServiceProvider);
            }
            catch (Exception ex)
            {
                ServiceProvider.GetRequiredService<NetMediateFixture>().RunError = ex;
            }
            finally
            {
                terminator.Terminate();
            }
        }
    }

    private record Terminator(Action Terminate);

    private record Runner(Func<IServiceProvider, Task> RunAsync);
}
