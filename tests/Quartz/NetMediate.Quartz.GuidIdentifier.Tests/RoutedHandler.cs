namespace NetMediate.Quartz.GuidIdentifier.Tests;

[Injectable(Key = "keyed")]
internal sealed class RoutedHandler(ITestNotifier fixture) : INotificationHandler<QuartzMessage>
{
    public Task Handle(QuartzMessage message, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Handler called. Method={message.Method}, Value={message.Value}");
        fixture.CheckValueFor(message.Method, message.Value);
        return Task.CompletedTask;
    }
}
