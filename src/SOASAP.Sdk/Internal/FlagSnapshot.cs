using System.Text.Json;

namespace Soasap.Sdk.Internal;

/// <summary>
/// Immutable in-memory view of all flags for one environment.
/// Intentionally not disposable: old snapshots remain valid for threads still reading until GC collects them.
/// </summary>
internal sealed class FlagSnapshot
{
    public FlagSnapshot(JsonDocument document, Dictionary<string, JsonElement> flags)
    {
        Document = document;
        Flags = flags;
    }

    /// <summary>Keeps <see cref="JsonElement"/> values in <see cref="Flags"/> alive.</summary>
    public JsonDocument Document { get; }

    /// <summary>Flag key to value map (ordinal case-sensitive keys).</summary>
    public Dictionary<string, JsonElement> Flags { get; }
}
