using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Soasap.Sdk.Internal;

namespace Soasap.Sdk;

/// <summary>
/// Maintains a long-lived SSE connection to <c>/api/v1/flags/live</c> and pushes JSON payloads to the client.
/// Reconnects automatically with exponential backoff; never throws to the caller of <see cref="RunAsync"/>.
/// </summary>
internal sealed class SoasapSseWorker
{
    private readonly SOASAPOptions _options;
    private readonly HttpClient _httpClient;
    private readonly Action<SoasapErrorContext> _onError;
    private readonly Action<string> _onPayload;
    private readonly ReconnectBackoff _backoff = new();

    public SoasapSseWorker(
        SOASAPOptions options,
        HttpClient httpClient,
        Action<SoasapErrorContext>? onError,
        Action<string> onPayload)
    {
        _options = options;
        _httpClient = httpClient;
        _onError = onError ?? (_ => { });
        _onPayload = onPayload;
    }

    /// <summary>
    /// Runs until <paramref name="cancellationToken"/> is cancelled, reconnecting on errors or stream end.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var disconnected = await ConnectAndReadAsync(cancellationToken).ConfigureAwait(false);
                _backoff.Reset();

                // Normal server close: wait before reconnecting (no OnError — expected for long-polling SSE).
                if (disconnected && !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(_backoff.NextDelay(), cancellationToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _onError(new SoasapErrorContext(ex, SoasapErrorSource.Network, isTransient: true));
                await Task.Delay(_backoff.NextDelay(), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Opens the SSE stream and processes events until disconnect or cancellation.
    /// </summary>
    /// <returns><c>true</c> if the stream ended and a reconnect should be attempted.</returns>
    private async Task<bool> ConnectAndReadAsync(CancellationToken cancellationToken)
    {
        var requestUri = $"{_options.BaseUrl.TrimEnd('/')}/api/v1/flags/live";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
#if NET5_0_OR_GREATER
        // SSE is most reliable over HTTP/1.1 with a persistent connection.
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionExact;
#endif
        request.Headers.TryAddWithoutValidation("X-API-Key", _options.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.ConnectionClose = false;

        using var response = await _httpClient
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);

        var parser = new SseEventParser();

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line;
                try
                {
                    line = await reader.ReadLineAsync().ConfigureAwait(false);
                }
                catch (IOException ex) when (IsConnectionClosed(ex))
                {
                    return true;
                }

                if (line is null)
                {
                    return true;
                }

                if (parser.AppendLine(line, out var exceededMemoryCap))
                {
                    if (exceededMemoryCap)
                    {
                        _onError(new SoasapErrorContext(
                            new InvalidOperationException(
                                $"SSE payload exceeded {SseEventParser.MaxBufferChars} character limit."),
                            SoasapErrorSource.Parser,
                            isTransient: true));
                        throw new InvalidOperationException("SSE memory cap exceeded.");
                    }

                    var payload = parser.TakePayload();
                    if (!string.IsNullOrWhiteSpace(payload))
                    {
                        ProcessPayload(payload);
                    }

                    parser.Reset();
                }
            }
        }
        catch (IOException ex) when (IsConnectionClosed(ex))
        {
            return true;
        }

        return false;
    }

    /// <summary>Detects remote host closing the TCP connection (common on Windows).</summary>
    private static bool IsConnectionClosed(IOException ex)
    {
        return ex.InnerException is SocketException;
    }

    /// <summary>Validates JSON and forwards the raw payload to <see cref="SOASAPClient"/>.</summary>
    private void ProcessPayload(string payload)
    {
        if (!FlagSnapshotFactory.TryFromJson(payload, out _))
        {
            _onError(new SoasapErrorContext(
                new JsonException("Invalid flags payload: root must be a JSON object."),
                SoasapErrorSource.Parser,
                isTransient: false));
            return;
        }

        _onPayload(payload);
    }
}
