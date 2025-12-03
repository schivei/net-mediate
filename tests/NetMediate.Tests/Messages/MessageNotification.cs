namespace NetMediate.Tests.Messages;

internal sealed record MessageNotification(int CommandId) : BaseMessage, INotification<MessageNotification>;
