namespace NetMediate.Quartz.MessageIdentifier.Tests;

internal sealed record QuartzMessage(int Value, string Method, string? Identifier = null, string? GroupName = null) : IQuartzMessage;
