using System.Text.Json;

namespace Soasap.Sdk.Internal;

/// <summary>
/// Parses flat JSON flag payloads into <see cref="FlagSnapshot"/> instances.
/// </summary>
internal static class FlagSnapshotFactory
{
    /// <summary>
    /// Parses JSON whose root must be a JSON object (<c>{}</c>). Arrays and scalars are rejected.
    /// </summary>
    public static bool TryFromJson(string json, out FlagSnapshot? snapshot)
    {
        snapshot = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                document.Dispose();
                return false;
            }

            var flags = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                flags[property.Name] = property.Value;
            }

            snapshot = new FlagSnapshot(document, flags);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
