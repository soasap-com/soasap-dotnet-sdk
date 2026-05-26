using Microsoft.Extensions.DependencyInjection;

namespace Soasap.Sdk;

/// <summary>
/// Registers the SOASAP feature flags client with dependency injection.
/// </summary>
public static class SOASAPServiceCollectionExtensions
{
    /// <summary>
    /// Adds a singleton <see cref="ISOASAPClient"/> in lazy mode: no network calls until the first flag read
    /// (unless <see cref="SOASAPBuilder.PreloadFlags"/> is chained).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="apiKey">Environment API key.</param>
    /// <param name="baseUrl">Public API base URL.</param>
    /// <returns>A builder for optional configuration.</returns>
    public static SOASAPBuilder AddSoasap(
        this IServiceCollection services,
        string apiKey,
        string baseUrl = "https://api.soasap.com")
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }

        var options = new SOASAPOptions(apiKey)
        {
            BaseUrl = baseUrl,
        };

        services.AddSingleton(options);
        services.AddSingleton<ISOASAPClient, SOASAPClient>();

        return new SOASAPBuilder(services, options);
    }
}
