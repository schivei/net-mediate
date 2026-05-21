namespace NetMediate.Quartz.Tests.Messages;

internal sealed record TestMessage(int Value)
{
    public int CheckValue { get; set; }
}
