using System.ComponentModel.DataAnnotations;

namespace NetMediate;

/// <summary>
/// Message pack for notifications and commands
/// </summary>
public interface IPack : IPack<ValueTask>;

/// <summary>
/// Message pack for requests and streams
/// </summary>
public interface IPack<out TResult> where TResult : notnull
{
    /// <summary>
    /// Send message with resulting
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    TResult Dispatch(CancellationToken cancellationToken);
    
    /// <summary>
    /// Validate message with validation handlers
    /// </summary>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    ValueTask<ValidationResult> ValidateAsync(CancellationToken cancellationToken);
}
