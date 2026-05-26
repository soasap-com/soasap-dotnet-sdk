namespace Soasap.Sdk.Internal;

/// <summary>
/// Creates an <see cref="HttpClient"/> tuned for long-lived SSE connections.
/// </summary>
internal static class SoasapHttpClientFactory
{
    /// <summary>
    /// Builds a client with infinite request timeout and (on .NET 6+) TCP keep-alive pings.
    /// </summary>
    public static HttpClient Create()
    {
#if NET6_0_OR_GREATER
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            KeepAlivePingDelay = TimeSpan.FromSeconds(15),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(5),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };
#else
        var handler = new HttpClientHandler();
#endif

        return new HttpClient(handler, disposeHandler: true)
        {
            // Required: default 100s timeout would kill SSE streams.
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }
}
