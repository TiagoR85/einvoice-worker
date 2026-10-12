namespace Einvoice.Worker.Domain;

public sealed class PermanentProcessingException(string message, Exception? inner = null)
    : Exception(message, inner);

public sealed class RetryableProcessingException(string message, Exception? inner = null)
    : Exception(message, inner);
