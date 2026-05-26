using System.Threading.Channels;

namespace Soasap.Sdk;

/// <summary>
/// Debounces disk writes: keeps only the latest JSON in a size-1 channel (<see cref="BoundedChannelFullMode.DropOldest"/>)
/// and flushes at most every <see cref="DebounceInterval"/>.
/// </summary>
internal sealed class DiskWriteCoalescer
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(2.5);

    private readonly FileCacheService _fileCache;
    private readonly Action<SoasapErrorContext> _onError;
    private readonly Channel<string> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _loopTask;

    public DiskWriteCoalescer(FileCacheService fileCache, Action<SoasapErrorContext>? onError)
    {
        _fileCache = fileCache;
        _onError = onError ?? (_ => { });

        _channel = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });

        _loopTask = Task.Run(() => DiskWriteLoopAsync(_cts.Token));
    }

    /// <summary>Queues the latest JSON for a debounced write (older pending values are dropped).</summary>
    public void Enqueue(string json)
    {
        _channel.Writer.TryWrite(json);
    }

    /// <summary>
    /// Completes the channel, drains the final payload to disk, and waits for the background loop to exit.
    /// </summary>
    public async Task CompleteAsync(TimeSpan timeout)
    {
        _channel.Writer.TryComplete();

        using var timeoutCts = new CancellationTokenSource(timeout);
        try
        {
#if NET6_0_OR_GREATER
            await _loopTask.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
#else
            var completed = await Task.WhenAny(_loopTask, Task.Delay(timeout, timeoutCts.Token)).ConfigureAwait(false);
            if (completed != _loopTask)
            {
                _cts.Cancel();
            }
            else
            {
                await _loopTask.ConfigureAwait(false);
            }
#endif
        }
        catch (OperationCanceledException)
        {
            _cts.Cancel();
        }
    }

    /// <summary>Signals cancellation without waiting (used on abnormal shutdown paths).</summary>
    public void Cancel()
    {
        _cts.Cancel();
        _channel.Writer.TryComplete();
    }

    private async Task DiskWriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (await _channel.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (_channel.Reader.TryRead(out var json))
                {
                    // Coalesce burst SSE updates into the newest payload only.
                    var latest = json;
                    while (_channel.Reader.TryRead(out var newer))
                    {
                        latest = newer;
                    }

                    try
                    {
                        _fileCache.Save(latest);
                    }
                    catch (Exception ex)
                    {
                        _onError(new SoasapErrorContext(ex, SoasapErrorSource.Disk, isTransient: true));
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(DebounceInterval, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    // Anything that arrived during the debounce window.
                    while (_channel.Reader.TryRead(out var pending))
                    {
                        latest = pending;
                        try
                        {
                            _fileCache.Save(latest);
                        }
                        catch (Exception ex)
                        {
                            _onError(new SoasapErrorContext(ex, SoasapErrorSource.Disk, isTransient: true));
                        }
                    }
                }
            }

            // Graceful shutdown: persist the last item after Writer.Complete().
            while (_channel.Reader.TryRead(out var finalJson))
            {
                try
                {
                    _fileCache.Save(finalJson);
                }
                catch (Exception ex)
                {
                    _onError(new SoasapErrorContext(ex, SoasapErrorSource.Disk, isTransient: true));
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }
}
