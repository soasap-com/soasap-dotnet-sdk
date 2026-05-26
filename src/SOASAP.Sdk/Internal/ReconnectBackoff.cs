namespace Soasap.Sdk.Internal;

/// <summary>
/// Exponential reconnect delays with jitter to avoid thundering herd on server recovery.
/// </summary>
internal sealed class ReconnectBackoff
{
    private static readonly int[] DelaysSeconds = { 1, 2, 5, 10, 30 };

#if !NET6_0_OR_GREATER
    private static readonly Random JitterRandom = new();
#endif

    private int _attempt;

    /// <summary>Resets the attempt counter after a successful connection.</summary>
    public void Reset() => _attempt = 0;

    /// <summary>
    /// Returns the delay before the next reconnect attempt (1s → 2s → 5s → 10s → 30s cap, ±200ms jitter).
    /// </summary>
    public TimeSpan NextDelay()
    {
        var index = Math.Min(_attempt, DelaysSeconds.Length - 1);
        _attempt++;

        var baseMs = DelaysSeconds[index] * 1000;
#if NET6_0_OR_GREATER
        var jitter = Random.Shared.Next(-200, 201);
#else
        var jitter = JitterRandom.Next(-200, 201);
#endif
        var totalMs = Math.Max(0, baseMs + jitter);
        return TimeSpan.FromMilliseconds(totalMs);
    }
}
