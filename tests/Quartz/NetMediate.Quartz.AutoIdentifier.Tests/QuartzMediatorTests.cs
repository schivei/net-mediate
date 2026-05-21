namespace NetMediate.Quartz.AutoIdentifier.Tests;

public sealed class AutoIdentifierTests
{
    [Fact]
    public async Task Handle()
    {
        var fixture = new AutoIdentifierFixture();
        await fixture.InitializeAsync();

        var method = GetType().Name + "::" + nameof(Handle);
        var counter = new CountdownEvent(9);
        fixture.TestNotifier.RegisterSemaphore(method, counter);

        TestMessage[] msgs = [
            new(1, method),
            new(2, method),
            new(3, method)
        ];

        var solo = new TestMessage(0, method);
        var solo2 = new QuartzMessage(0, method, "teste");

        fixture.Mediator.Notify("keyed", solo);
        fixture.Mediator.Notifies<TestMessage>("keyed", []);
        fixture.Mediator.Notifies("keyed", msgs);

        fixture.Mediator.Notify(solo);
        fixture.Mediator.Notify(solo2);
        fixture.Mediator.Notify(solo2);
        fixture.Mediator.Notifies<TestMessage>([]);
        fixture.Mediator.Notifies(msgs);

        counter.Wait(TestContext.Current.CancellationToken);

        Assert.True(12 >= fixture.TestNotifier.GetValue(method));
        Assert.True(8 <= fixture.TestNotifier.GetValue(method));
    }
}
