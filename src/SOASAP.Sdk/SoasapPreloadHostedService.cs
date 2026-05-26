using Microsoft.Extensions.Hosting;

namespace Soasap.Sdk;

/// <summary>
/// Opt-in hosted service that triggers the SSE worker at application startup via <see cref="SOASAPBuilder.PreloadFlags"/>.
/// </summary>
internal sealed class SoasapPreloadHostedService(ISOASAPClient client) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Fire-and-forget: must not await network I/O so host startup is not blocked.
        if (client is SOASAPClient soasapClient)
        {
            soasapClient.EnsureWorkerStarted();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
