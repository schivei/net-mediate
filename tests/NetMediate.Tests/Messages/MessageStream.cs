namespace NetMediate.Tests.Messages;

internal sealed record MessageStream(int CommandId) : BaseMessage, IStream<int>;
