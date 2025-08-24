using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Moq;
using NetMediate.Internals;
using NetMediate.Internals.Workers;

namespace NetMediate.Tests.Internals.Workers;

public sealed class NotificationWorkerAdditionalTests
{
    private static void VerifyLog(
        Mock<ILogger<NotificationWorker>> logger,
        LogLevel level,
        string contains
    )
    {
        logger.Verify(
            x =>
                x.Log(
                    It.Is<LogLevel>(l => l == level),
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((v, t) => v!.ToString()!.Contains(contains)),
                    It.IsAny<Exception?>(),
                    It.Is<Func<It.IsAnyType, Exception?, string>>((_, _) => true)
                ),
            Times.AtLeastOnce
        );
    }

    [Fact]
    public async Task NullMessage_IsSkipped_NoDispatch()
    {
        var ch = Channel.CreateUnbounded<INotificationPacket>();
        var cfg = new Configuration(ch);
        var mediator = new Mock<INotifiable>(MockBehavior.Strict);
        var logger = new Mock<ILogger<NotificationWorker>>();

        var worker = new NotificationWorker(mediator.Object, cfg, logger.Object);

        await worker.StartAsync(CancellationToken.None);

        // enqueue a null message packet
        var packet = new NotificationPacket<string?>(null, (_, _, _) => Task.CompletedTask);
        await cfg.ChannelWriter.WriteAsync(packet);

        // small delay to allow processing
        await Task.Delay(50);

        // mediator.Notifies must not be called for null message
        mediator.Verify(
            m => m.Notifies(It.IsAny<INotificationPacket>(), It.IsAny<CancellationToken>()),
            Times.Never
        );

        await worker.StopAsync(CancellationToken.None);
        VerifyLog(logger, LogLevel.Debug, "Notification worker stopped.");
    }

    [Fact]
    public async Task Logs_Trace_When_Notifies_Throws()
    {
        var channel = Channel.CreateUnbounded<INotificationPacket>();
        var config = new Configuration(channel);
        var mediator = new Mock<INotifiable>();
        var logger = new Mock<ILogger<NotificationWorker>>();

        mediator
            .Setup(m => m.Notifies(It.IsAny<INotificationPacket>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var worker = new NotificationWorker(mediator.Object, config, logger.Object);

        await worker.StartAsync(CancellationToken.None);

        var packet = new NotificationPacket<string>("msg", (_, _, _) => Task.CompletedTask);
        await config.ChannelWriter.WriteAsync(packet);

        await Task.Delay(50);

        VerifyLog(logger, LogLevel.Trace, "An error occurred while processing message of type");

        await worker.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Runs_And_Stops_Cleanly()
    {
        var channel = Channel.CreateUnbounded<INotificationPacket>();
        var config = new Configuration(channel);
        var mediator = new Mock<INotifiable>();
        mediator
            .Setup(m => m.Notifies(It.IsAny<INotificationPacket>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var logger = new Mock<ILogger<NotificationWorker>>();
        var worker = new NotificationWorker(mediator.Object, config, logger.Object);

        await worker.StartAsync(CancellationToken.None);

        var packet = new NotificationPacket<string>("ok", (_, _, _) => Task.CompletedTask);
        await config.ChannelWriter.WriteAsync(packet);

        await Task.Delay(50);

        mediator.Verify(
            m => m.Notifies(It.IsAny<INotificationPacket>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce
        );

        await worker.StopAsync(CancellationToken.None);
        VerifyLog(logger, LogLevel.Debug, "Notification worker stopped.");
    }
}
