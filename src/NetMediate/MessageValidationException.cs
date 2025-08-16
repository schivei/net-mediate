namespace NetMediate;

/// <summary>
/// Represents an exception that is thrown when a message fails validation.
/// </summary>
/// <param name="message">The error message that explains the reason for the exception.</param>
public sealed class MessageValidationException(string message) : Exception(message);
