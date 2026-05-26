namespace Soasap.Sdk;

/// <summary>
/// Configuration for <see cref="SOASAPClient"/>.
/// </summary>
public sealed class SOASAPOptions
{
    /// <summary>
    /// Initializes options with the environment API key (sent as <c>X-API-Key</c> on SSE requests).
    /// </summary>
    public SOASAPOptions(string apiKey)
    {
        ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }
    }

    /// <summary>Environment API key for the public flags API.</summary>
    public string ApiKey { get; }

    /// <summary>Base URL of the SOASAP public API (without trailing slash).</summary>
    public string BaseUrl { get; set; } = "https://api.soasap.com";

    /// <summary>
    /// Optional directory for the on-disk cache file. When null, uses LocalApplicationData or BaseDirectory.
    /// </summary>
    public string? CacheDirectory { get; set; }

    /// <summary>Optional handler for background errors (network, disk, parser).</summary>
    public Action<SoasapErrorContext>? OnError { get; set; }

    /// <summary>
    /// Set by <see cref="SOASAPBuilder.PreloadFlags"/> to register an <see cref="Microsoft.Extensions.Hosting.IHostedService"/>
    /// that starts the SSE worker at application startup.
    /// </summary>
    public bool PreloadFlags { get; set; }
}
