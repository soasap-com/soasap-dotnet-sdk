namespace Soasap.Sdk;

/// <summary>
/// Identifies which SDK subsystem reported an error.
/// </summary>
public enum SoasapErrorSource
{
    /// <summary>SSE connection, HTTP, or transport failures.</summary>
    Network,

    /// <summary>Reading or writing the on-disk flag cache.</summary>
    Disk,

    /// <summary>Invalid or unexpected SSE/JSON payload.</summary>
    Parser,
}
