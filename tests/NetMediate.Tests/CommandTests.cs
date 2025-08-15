//namespace NetMediate.Tests;

//public sealed class CommandTests(NetMediateFixture fixture) : IClassFixture<NetMediateFixture>
//{
//    [Fact]
//    public void Command_Execute_ShouldReturnExpectedResult()
//    {
//        // Arrange
//        var command = new SampleCommand();
//        var expected = "Command executed successfully";
//        // Act
//        var result = command.Execute();
//        // Assert
//        Assert.Equal(expected, result);
//    }

//    [Fact]
//    public void Command_Execute_WithError_ShouldThrowException()
//    {
//        // Arrange
//        var command = new FaultyCommand();
//        // Act & Assert
//        Assert.Throws<InvalidOperationException>(() => command.Execute());
//    }
//}
