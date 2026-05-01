namespace NetMediate;

/// <summary>
/// Defines a contract for terminating an operation or process.
/// </summary>
/// <remarks>Implementations should provide logic to safely and reliably terminate the associated operation. The
/// specific behavior of termination depends on the implementing type and may include resource cleanup or cancellation
/// of ongoing work.</remarks>
public interface ITerminator
{
    /// <summary>
    /// Performs a termination operation, ending the current process or activity.
    /// </summary>
    /// <remarks>Call this method to explicitly terminate the associated process or workflow. The specific
    /// effects of termination depend on the implementation. After calling this method, further operations may not be
    /// possible.</remarks>
    void Terminate();
}
