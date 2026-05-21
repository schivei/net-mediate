namespace NetMediate.Quartz.HashIdentifier.Tests;

[Injectable(Key = "keyed")]
internal sealed class KeyedHandler(ITestNotifier fixture) : INotificationHandler<TestMessage>
{
    public ValueTask Handle(TestMessage message, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Handler called. Method={message.Method}, Value={message.Value}");
        fixture.CheckValueFor(message.Method, message.Value);
        return ValueTask.CompletedTask;
    }
}
