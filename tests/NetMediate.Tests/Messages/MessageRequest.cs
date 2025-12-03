namespace NetMediate.Tests.Messages;

internal sealed record MessageRequest(int CommandId) : BaseMessage, IRequest<MessageRequest, int>;
