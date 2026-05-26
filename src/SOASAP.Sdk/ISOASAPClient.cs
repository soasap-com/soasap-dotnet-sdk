namespace Soasap.Sdk;

/// <summary>
/// Thread-safe client for reading feature flags from an in-memory snapshot.
/// All getters are synchronous, O(1), and never perform network or disk I/O.
/// </summary>
public interface ISOASAPClient
{
    /// <summary>
    /// Returns the boolean value of a flag, or <paramref name="defaultValue"/> if the key is missing or not a boolean.
    /// </summary>
    bool GetBool(string flagKey, bool defaultValue = false);

    /// <summary>
    /// Returns the numeric value of a flag as <see cref="double"/>, or <paramref name="defaultValue"/> if the key is missing or not a number.
    /// </summary>
    double GetNumber(string flagKey, double defaultValue = 0.0);

    /// <summary>
    /// Returns the string value of a flag, or <paramref name="defaultValue"/> if the key is missing or not a string.
    /// </summary>
    string GetString(string flagKey, string defaultValue = "");

    /// <summary>
    /// Deserializes a JSON object or array flag into <typeparamref name="T"/>, or returns <paramref name="defaultValue"/> on failure.
    /// JSON property names are matched case-insensitively (e.g. <c>theme</c> maps to <c>Theme</c>).
    /// </summary>
    T? GetJson<T>(string flagKey, T? defaultValue = default);

    /// <summary>
    /// Alias for <see cref="GetBool"/> for boolean feature toggles.
    /// </summary>
    bool GetFlag(string flagKey, bool defaultValue = false);
}
