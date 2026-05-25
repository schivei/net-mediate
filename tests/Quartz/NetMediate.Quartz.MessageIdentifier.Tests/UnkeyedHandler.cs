namespace NetMediate.Quartz.MessageIdentifier.Tests;

[Injectable]
internal sealed class UnkeyedHandler(ITestNotifier fixture) : INotificationHandler<QuartzMessage>
{
    public Task Handle(QuartzMessage message, CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Handler called. Method={message.Method}, Value={message.Value}");
        fixture.CheckValueFor(message.Method, message.Value);
        return Task.CompletedTask;
    }
}
