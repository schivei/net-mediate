namespace NetMediate.Quartz.GuidIdentifier.Tests;

internal sealed record QuartzMessage(int Value, string Method, string? Identifier = null, string? GroupName = null) : IQuartzMessage;
