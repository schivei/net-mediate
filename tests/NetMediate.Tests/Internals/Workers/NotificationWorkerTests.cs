using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Moq;
using NetMediate.Internals;
using NetMediate.Internals.Workers;

namespace NetMediate.Tests.Internals.Workers;

public class NotificationWorkerTests
{
    private readonly Mock<MediatorTest> _mediatorMock;
    private readonly Mock<ILogger<NotificationWorker>> _loggerMock;
    private readonly Channel<INotificationPacket> _channel;
    private readonly Channel<INotificationPacket> _channel2;
    private readonly Configuration _configuration;
    private readonly Configuration _configuration2;
    private readonly NotificationWorker _worker;
    private readonly NotificationWorker _worker2;

    public NotificationWorkerTests()
    {
        _loggerMock = new Mock<ILogger<NotificationWorker>>();
        _channel = Channel.CreateUnbounded<INotificationPacket>();
        _channel2 = Channel.CreateUnbounded<INotificationPacket>();
        _configuration = new Configuration(_channel);
        _configuration2 = new Configuration(_channel2);
        _mediatorMock = new Mock<MediatorTest>() { CallBase = true };
        _worker = new NotificationWorker(_mediatorMock.Object, _configuration, _loggerMock.Object);
        _worker2 = new NotificationWorker(
            _mediatorMock.Object,
            _configuration2,
            _loggerMock.Object
        );
    }

    private static INotificationPacket Pack<TMessage>(
        TMessage message,
        NotificationErrorDelegate<TMessage>? onError = null
    ) => new NotificationPacket<TMessage>(message, onError ?? ((_, _, _) => Task.CompletedTask));

    [Fact]
    public async Task ExecuteAsync_ProcessesMessages_Successfully()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        var pack = Pack(message);
        await _channel.Writer.WriteAsync(pack);
        _channel.Writer.Complete();
        var cts = new CancellationTokenSource();

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow time for processing
        await _worker.StopAsync(cts.Token);

        // Assert
        _mediatorMock.Verify(m => m.Notifies(pack, It.IsAny<CancellationToken>()), Times.Once);
        VerifyDebugLog("Processing message of type TestMessage", Times.Once());
    }

    [Fact]
    public async Task ExecuteAsync_SkipsNullMessages()
    {
        // Arrange
        await _channel.Writer.WriteAsync(null!);
        _channel.Writer.Complete();
        var cts = new CancellationTokenSource();

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow time for processing
        await _worker.StopAsync(cts.Token);

        // Assert
        _mediatorMock.Verify(
            m => m.Notifies(It.IsAny<INotificationPacket>(), It.IsAny<CancellationToken>()),
            Times.Never
        );
    }

    [Fact]
    public async Task ExecuteAsync_HandlesOperationCanceledException()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        var pack = Pack(message);
        _mediatorMock
            .Setup(m => m.Notifies(pack, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());
        var cts = new CancellationTokenSource();

        await _channel.Writer.WriteAsync(pack);

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100); // Allow time for processing

        // Assert
        VerifyDebugLog(
            "An error occurred while processing message of type TestMessage",
            Times.Once(),
            LogLevel.Trace
        );
        await _worker.StopAsync(cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesChannelClosedException()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        var pack = Pack(message);
        _mediatorMock
            .Setup(m => m.Notifies(pack, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ChannelClosedException());
        var cts = new CancellationTokenSource();

        await _channel.Writer.WriteAsync(pack);

        // Act
        var task = _worker.StartAsync(cts.Token);
        await Task.Delay(100);

        // Assert
        VerifyDebugLog(
            "An error occurred while processing message of type TestMessage",
            Times.Once(),
            LogLevel.Trace
        );
        await _worker.StopAsync(cts.Token);
    }

    [Fact]
    public async Task ExecuteAsync_StopsWhenCancellationRequested()
    {
        // Arrange
        var message = new TestMessage { Id = 1 };
        var pack = Pack(message);
        await _channel2.Writer.WriteAsync(pack);
        var cts = new CancellationTokenSource();
        cts.CancelAfter(50);

        // Act
        await _worker2.StartAsync(cts.Token);
        await Task.Delay(500);

        // Assert
        VerifyDebugLog("Notification worker stopped.", Times.Once());
    }

    private void VerifyDebugLog(
        string messageContains,
        Times times,
        LogLevel level = LogLevel.Debug
    )
    {
        _loggerMock.Verify(
            x =>
                x.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(messageContains)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            times
        );
    }

    private void VerifyLog(LogLevel level, string messageContains, Times times)
    {
        _loggerMock.Verify(
            x =>
                x.Log(
                    level,
                    It.IsAny<EventId>(),
                    It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains(messageContains)),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception?, string>>()
                ),
            times
        );
    }

    public class TestMessage
    {
        public int Id { get; set; }
    }

    public class MediatorTest : IMediator, INotifiable
    {
        public Task Notify<TMessage>(
            TMessage message,
            NotificationErrorDelegate<TMessage> onError,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        public virtual Task<TResponse> Request<TMessage, TResponse>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<TResponse>(default!);

        public virtual async IAsyncEnumerable<TResponse> RequestStream<TMessage, TResponse>(
            TMessage message,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.Yield();

            yield break;
        }

        public virtual Task Send<TMessage>(
            TMessage message,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        internal virtual Task Handle(
            INotificationPacket packet,
            CancellationToken cancellationToken = default
        ) => Task.CompletedTask;

        Task INotifiable.Notifies(
            INotificationPacket packet,
            CancellationToken cancellationToken
        ) => Notifies(packet, cancellationToken);

        internal virtual async Task Notifies(
            INotificationPacket packet,
            CancellationToken cancellationToken = default
        )
        {
            try
            {
                await Handle(packet, cancellationToken);
            }
            catch (Exception ex)
            {
                await packet.OnErrorAsync(packet.Message.GetType(), ex);
            }
        }

        Task IMediator.Notify<TMessage>(INotification<TMessage> notification, NotificationErrorDelegate<TMessage> onError, CancellationToken cancellationToken) =>
            Notify((TMessage)notification, onError, cancellationToken);

        Task IMediator.Send<TMessage>(ICommand<TMessage> command, CancellationToken cancellationToken) =>
            Send((TMessage)command, cancellationToken);

        Task<TResponse> IMediator.Request<TMessage, TResponse>(IRequest<TMessage, TResponse> request, CancellationToken cancellationToken) =>
            Request<TMessage, TResponse>((TMessage)request, cancellationToken);

        IAsyncEnumerable<TResponse> IMediator.RequestStream<TMessage, TResponse>(IStream<TMessage, TResponse> request, CancellationToken cancellationToken) =>
            RequestStream<TMessage, TResponse>((TMessage)request, cancellationToken);
    }
}
