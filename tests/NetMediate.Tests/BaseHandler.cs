namespace NetMediate.Tests;

internal abstract class BaseHandler
{
    protected static T Marks<T>(T message)
        where T : BaseMessage
    {
        message.Run();
        return message;
    }
}
