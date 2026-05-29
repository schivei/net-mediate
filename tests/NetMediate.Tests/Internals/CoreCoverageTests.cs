namespace NetMediate.Tests.Internals;

/// <summary>
/// Targets specific lines/branches in src/NetMediate that are not exercised by other test classes.
/// </summary>
public sealed class CoreCoverageTests
{
    [Fact]
    public void MediatorException_Ctor_ExposesAllProperties()
    {
        var inner = new InvalidOperationException("handler error");

        var ex = new MediatorException(
            typeof(string),
            typeof(ICommandHandler<string>),
            "trace-42",
            inner
        );

        Assert.Equal(typeof(string), ex.MessageType);
        Assert.Equal(typeof(ICommandHandler<string>), ex.HandlerType);
        Assert.Equal("trace-42", ex.TraceId);
        Assert.Same(inner, ex.InnerException);
        Assert.IsType<Exception>(ex, exactMatch: false);
        Assert.Contains("String", ex.Message);
    }

    [Fact]
    public void MediatorException_WithNullHandlerType_MessageExcludesHandlerType()
    {
        var inner = new Exception("fail");

        var ex = new MediatorException(typeof(int), null, null, inner);

        Assert.Null(ex.HandlerType);
        Assert.Null(ex.TraceId);
        Assert.Contains("Int32", ex.Message);
    }
}
