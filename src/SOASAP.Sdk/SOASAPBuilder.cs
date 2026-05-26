using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Soasap.Sdk;

/// <summary>
/// Fluent configuration returned by <see cref="SOASAPServiceCollectionExtensions.AddSoasap"/>.
/// </summary>
public sealed class SOASAPBuilder
{
    internal SOASAPBuilder(IServiceCollection services, SOASAPOptions options)
    {
        Services = services;
        Options = options;
    }

    /// <summary>The service collection being configured.</summary>
    public IServiceCollection Services { get; }

    internal SOASAPOptions Options { get; }

    /// <summary>
    /// Starts the SSE background worker when the host starts, without blocking startup (no network await in <c>StartAsync</c>).
    /// </summary>
    public SOASAPBuilder PreloadFlags()
    {
        Options.PreloadFlags = true;
        Services.AddHostedService<SoasapPreloadHostedService>();
        return this;
    }

    /// <summary>Alias for <see cref="PreloadFlags"/>.</summary>
    public SOASAPBuilder UseBackgroundWorker() => PreloadFlags();

    /// <summary>Overrides the default on-disk cache directory.</summary>
    public SOASAPBuilder WithCacheDirectory(string cacheDirectory)
    {
        Options.CacheDirectory = cacheDirectory;
        return this;
    }

    /// <summary>
    /// Registers an error handler. Multiple calls are chained (all handlers are invoked).
    /// </summary>
    public SOASAPBuilder OnError(Action<SoasapErrorContext> handler)
    {
        var previous = Options.OnError;
        Options.OnError = previous is null
            ? handler
            : ctx =>
            {
                previous(ctx);
                handler(ctx);
            };
        return this;
    }
}
