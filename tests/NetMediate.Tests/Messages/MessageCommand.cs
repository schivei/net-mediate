namespace NetMediate.Tests.Messages;

internal sealed record MessageCommand(int CommandId) : BaseMessage, ICommand<MessageCommand>;
