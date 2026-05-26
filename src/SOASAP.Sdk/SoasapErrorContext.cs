namespace Soasap.Sdk;

/// <summary>
/// Structured error information passed to <see cref="SOASAPOptions.OnError"/>.
/// </summary>
public sealed class SoasapErrorContext
{
    /// <summary>
    /// Creates a new error context with the current UTC timestamp.
    /// </summary>
    public SoasapErrorContext(Exception exception, SoasapErrorSource source, bool isTransient)
    {
        Exception = exception;
        Source = source;
        IsTransient = isTransient;
        Timestamp = DateTimeOffset.UtcNow;
    }

    /// <summary>The underlying exception.</summary>
    public Exception Exception { get; }

    /// <summary>Subsystem that produced the error.</summary>
    public SoasapErrorSource Source { get; }

    /// <summary>When the error was observed (UTC).</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>
    /// When true, the SDK will retry (e.g. reconnect SSE). The application can usually continue using the last cached flags.
    /// </summary>
    public bool IsTransient { get; }
}
