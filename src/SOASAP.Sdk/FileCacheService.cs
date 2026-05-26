using System.Security.Cryptography;
using System.Text;

namespace Soasap.Sdk;

/// <summary>
/// Persists the latest flat JSON flags payload to a single file per API key (cold-start warm cache).
/// </summary>
internal sealed class FileCacheService
{
    public FileCacheService(string apiKey, string? cacheDirectory)
    {
        CacheFilePath = BuildCacheFilePath(apiKey, cacheDirectory);
    }

    /// <summary>Full path to <c>soasap_cache_{hash}.json</c>.</summary>
    public string CacheFilePath { get; }

    /// <summary>
    /// Reads the cache file if it exists. Returns false on any I/O or empty file (never throws).
    /// </summary>
    public bool TryLoad(out string? json)
    {
        json = null;

        try
        {
            if (!File.Exists(CacheFilePath))
            {
                return false;
            }

            json = File.ReadAllText(CacheFilePath);
            return !string.IsNullOrWhiteSpace(json);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Atomically writes JSON via a temporary file (crash-safe replace).
    /// </summary>
    public void Save(string json)
    {
        var directory = Path.GetDirectoryName(CacheFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = CacheFilePath + ".tmp";
        File.WriteAllText(tempPath, json, Encoding.UTF8);
#if NET6_0_OR_GREATER
        File.Move(tempPath, CacheFilePath, overwrite: true);
#else
        if (File.Exists(CacheFilePath))
        {
            File.Delete(CacheFilePath);
        }

        File.Move(tempPath, CacheFilePath);
#endif
    }

    /// <summary>Builds <c>{cacheDir}/soasap_cache_{sha256First8}.json</c>.</summary>
    internal static string BuildCacheFilePath(string apiKey, string? cacheDirectory)
    {
        var hash = ComputeCacheId(apiKey);
        var directory = ResolveCacheDirectory(cacheDirectory);
        return Path.Combine(directory, $"soasap_cache_{hash}.json");
    }

    /// <summary>First 8 hex characters of SHA-256 over the full API key.</summary>
    internal static string ComputeCacheId(string apiKey)
    {
        var bytes = Encoding.UTF8.GetBytes(apiKey);
#if NET6_0_OR_GREATER
        var hash = SHA256.HashData(bytes);
#else
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(bytes);
#endif
        return ToHexLower(hash, 8);
    }

    private static string ToHexLower(byte[] hash, int hexLength)
    {
#if NET5_0_OR_GREATER
        return Convert.ToHexString(hash).AsSpan(0, hexLength).ToString().ToLowerInvariant();
#else
        var chars = new char[hexLength];
        for (var i = 0; i < hexLength / 2; i++)
        {
            var b = hash[i];
            chars[i * 2] = GetHexChar(b >> 4);
            chars[i * 2 + 1] = GetHexChar(b & 0xF);
        }

        return new string(chars).ToLowerInvariant();
#endif
    }

    private static char GetHexChar(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);

    private static string ResolveCacheDirectory(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured!;
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localAppData))
        {
            return Path.Combine(localAppData, "soasap", "cache");
        }

        return Path.Combine(AppContext.BaseDirectory, "soasap", "cache");
    }
}
