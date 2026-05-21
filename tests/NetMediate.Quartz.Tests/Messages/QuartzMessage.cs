namespace NetMediate.Quartz.Tests.Messages;

internal sealed record QuartzMessage(int Value, string? Identifier = null, string? GroupName = null) : IQuartzMessage
{
    public int CheckValue { get; set; }
}
