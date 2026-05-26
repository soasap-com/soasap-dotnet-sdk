using System.Text.Json;

namespace Soasap.Sdk.Internal;

/// <summary>
/// Shared <see cref="JsonSerializerOptions"/> for SDK deserialization.
/// </summary>
internal static class SoasapJsonSerializerOptions
{
    /// <summary>
    /// Used by <see cref="SOASAPClient.GetJson{T}"/> — JSON property names match CLR members case-insensitively.
    /// </summary>
    public static JsonSerializerOptions Deserialize { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
    };
}
