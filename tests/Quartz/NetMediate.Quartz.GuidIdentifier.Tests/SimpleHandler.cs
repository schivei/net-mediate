namespace NetMediate.Quartz.GuidIdentifier.Tests;

[Injectable]
internal sealed class SimpleHandler(ITestNotifier fixture) : INotificationHandler<TestMessage>
{
    public Task Handle(TestMessage message, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Handler called. Method={message.Method}, Value={message.Value}");
        fixture.CheckValueFor(message.Method, message.Value);
        return Task.CompletedTask;
    }
}
