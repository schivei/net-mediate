namespace NetMediate.Moq;

/// <summary>
/// Fluent extensions for common asynchronous mock setups.
/// </summary>
public static class MockExtensions
{
    /// <summary>
    /// Returns a completed task for setups that match asynchronous command handlers.
    /// </summary>
    /// <typeparam name="TMock">Mock target type.</typeparam>
    /// <param name="setup">Task setup expression.</param>
    /// <returns>Setup continuation for fluent assertions.</returns>
    public static global::Moq.Language.Flow.IReturnsResult<TMock> ReturnsCompletedTask<TMock>(
        this global::Moq.Language.Flow.ISetup<TMock, Task> setup
    ) where TMock : class => setup.Returns(Task.CompletedTask);

    /// <summary>
    /// Returns a constant value wrapped in a task.
    /// </summary>
    /// <typeparam name="TMock">Mock target type.</typeparam>
    /// <typeparam name="TResult">Result type returned by the setup.</typeparam>
    /// <param name="setup">Task setup expression.</param>
    /// <param name="value">Value to return.</param>
    /// <returns>Setup continuation for fluent assertions.</returns>
    public static global::Moq.Language.Flow.IReturnsResult<TMock> ReturnsTaskResult<TMock, TResult>(
        this global::Moq.Language.Flow.ISetup<TMock, Task<TResult>> setup,
        TResult value
    ) where TMock : class => setup.Returns(Task.FromResult(value));

    /// <summary>
    /// Returns a constant value wrapped in a value task.
    /// </summary>
    /// <typeparam name="TMock">Mock target type.</typeparam>
    /// <typeparam name="TResult">Result type returned by the setup.</typeparam>
    /// <param name="setup">Task setup expression.</param>
    /// <param name="value">Value to return.</param>
    /// <returns>Setup continuation for fluent assertions.</returns>
    public static global::Moq.Language.Flow.IReturnsResult<TMock> ReturnsTask<TMock, TResult>(
        this global::Moq.Language.Flow.ISetup<TMock, Task<TResult>> setup,
        TResult value
    ) where TMock : class => setup.Returns(new Task<TResult>(value));
}
