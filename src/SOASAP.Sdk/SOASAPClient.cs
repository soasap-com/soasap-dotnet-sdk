using System.Text.Json;
using Soasap.Sdk.Internal;

namespace Soasap.Sdk;

/// <summary>
/// Lock-free feature flags client: reads use <see cref="Volatile.Read"/> on an immutable <see cref="FlagSnapshot"/>,
/// writes atomically swap snapshots via <see cref="Interlocked.Exchange"/>.
/// </summary>
public sealed class SOASAPClient : ISOASAPClient, IAsyncDisposable, IDisposable
{
    /// <summary>Maximum time to wait for the disk writer to flush on shutdown.</summary>
    private static readonly TimeSpan DiskShutdownTimeout = TimeSpan.FromSeconds(5);

    private readonly SOASAPOptions _options;
    private readonly HttpClient _httpClient;
    private readonly FileCacheService _fileCache;
    private readonly DiskWriteCoalescer _diskWriter;
    private readonly CancellationTokenSource _workerCts = new();
    private readonly Action<SoasapErrorContext> _onError;

    /// <summary>Current flag snapshot; readers copy this reference locally (lock-free hot path).</summary>
    private FlagSnapshot? _currentSnapshot;

    /// <summary>0 = worker not started, 1 = worker started (see <see cref="EnsureWorkerStarted"/>).</summary>
    private int _workerStarted;

    private Task? _sseTask;
    private bool _disposed;

    /// <summary>
    /// Creates the client, loads the on-disk cache synchronously (no network), and starts the disk writer loop.
    /// </summary>
    public SOASAPClient(SOASAPOptions options)
    {
        _options = options;
        _onError = options.OnError ?? (_ => { });
        _httpClient = SoasapHttpClientFactory.Create();
        _fileCache = new FileCacheService(options.ApiKey, options.CacheDirectory);
        _diskWriter = new DiskWriteCoalescer(_fileCache, _onError);

        TryLoadDiskCache();
    }

    /// <inheritdoc />
    public bool GetBool(string flagKey, bool defaultValue = false)
    {
        EnsureWorkerStarted();
        var snapshot = Volatile.Read(ref _currentSnapshot);
        if (snapshot is null || !snapshot.Flags.TryGetValue(flagKey, out var element))
        {
            return defaultValue;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => defaultValue,
        };
    }

    /// <inheritdoc />
    public double GetNumber(string flagKey, double defaultValue = 0.0)
    {
        EnsureWorkerStarted();
        var snapshot = Volatile.Read(ref _currentSnapshot);
        if (snapshot is null || !snapshot.Flags.TryGetValue(flagKey, out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind != JsonValueKind.Number)
        {
            return defaultValue;
        }

        return element.GetDouble();
    }

    /// <inheritdoc />
    public string GetString(string flagKey, string defaultValue = "")
    {
        EnsureWorkerStarted();
        var snapshot = Volatile.Read(ref _currentSnapshot);
        if (snapshot is null || !snapshot.Flags.TryGetValue(flagKey, out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            return defaultValue;
        }

        return element.GetString() ?? defaultValue;
    }

    /// <inheritdoc />
    public bool GetFlag(string flagKey, bool defaultValue = false) => GetBool(flagKey, defaultValue);

    /// <inheritdoc />
    public T? GetJson<T>(string flagKey, T? defaultValue = default)
    {
        EnsureWorkerStarted();
        var snapshot = Volatile.Read(ref _currentSnapshot);
        if (snapshot is null || !snapshot.Flags.TryGetValue(flagKey, out var element))
        {
            return defaultValue;
        }

        if (element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array))
        {
            return defaultValue;
        }

        try
        {
#if NET5_0_OR_GREATER
            return JsonSerializer.Deserialize(element, typeof(T), SoasapJsonSerializerOptions.Deserialize) is T value
                ? value
                : defaultValue;
#else
            return JsonSerializer.Deserialize<T>(element.GetRawText(), SoasapJsonSerializerOptions.Deserialize)
                ?? defaultValue;
#endif
        }
        catch (JsonException)
        {
            return defaultValue;
        }
    }

    /// <summary>
    /// Starts the SSE background worker exactly once (lazy). Safe to call from any getter or hosted service preload.
    /// </summary>
    internal void EnsureWorkerStarted()
    {
        if (_disposed)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _workerStarted, 1, 0) != 0)
        {
            return;
        }

        var worker = new SoasapSseWorker(_options, _httpClient, _onError, OnSsePayload);
        _sseTask = Task.Run(() => RunSseWorkerSafeAsync(worker, _workerCts.Token));
    }

    /// <summary>Loads the last persisted JSON snapshot from disk into <see cref="_currentSnapshot"/>.</summary>
    private void TryLoadDiskCache()
    {
        try
        {
            if (!_fileCache.TryLoad(out var json) || json is null)
            {
                return;
            }

            if (FlagSnapshotFactory.TryFromJson(json, out var snapshot) && snapshot is not null)
            {
                Interlocked.Exchange(ref _currentSnapshot, snapshot);
            }
        }
        catch (Exception ex)
        {
            _onError(new SoasapErrorContext(ex, SoasapErrorSource.Disk, isTransient: false));
        }
    }

    /// <summary>Applies a validated SSE payload to memory and enqueues it for debounced disk persistence.</summary>
    private void OnSsePayload(string json)
    {
        if (!FlagSnapshotFactory.TryFromJson(json, out var snapshot) || snapshot is null)
        {
            _onError(new SoasapErrorContext(
                new JsonException("Invalid flags payload: root must be a JSON object."),
                SoasapErrorSource.Parser,
                isTransient: false));
            return;
        }

        Interlocked.Exchange(ref _currentSnapshot, snapshot);
        _diskWriter.Enqueue(json);
    }

    /// <summary>Top-level guard so unhandled exceptions never escape the background <see cref="Task"/>.</summary>
    private async Task RunSseWorkerSafeAsync(SoasapSseWorker worker, CancellationToken cancellationToken)
    {
        try
        {
            await worker.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected on shutdown.
        }
        catch (Exception ex)
        {
            _onError(new SoasapErrorContext(ex, SoasapErrorSource.Network, isTransient: true));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// Cancels the SSE worker, flushes the disk cache channel, and releases HTTP resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _workerCts.Cancel();

        if (_sseTask is not null)
        {
            try
            {
                await _sseTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
            catch (Exception ex)
            {
                _onError(new SoasapErrorContext(ex, SoasapErrorSource.Network, isTransient: false));
            }
        }

        await _diskWriter.CompleteAsync(DiskShutdownTimeout).ConfigureAwait(false);
        _httpClient.Dispose();
        _workerCts.Dispose();
    }
}
