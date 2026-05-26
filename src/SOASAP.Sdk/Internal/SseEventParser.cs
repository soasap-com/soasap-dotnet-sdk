using System.Text;

namespace Soasap.Sdk.Internal;

/// <summary>
/// Accumulates SSE <c>data:</c> lines until an empty line marks the end of an event.
/// </summary>
internal sealed class SseEventParser
{
    /// <summary>Maximum accumulated payload size (5 MB) before the connection is aborted (DoS protection).</summary>
    public const int MaxBufferChars = 5 * 1024 * 1024;

    private readonly StringBuilder _buffer = new();

    /// <summary>Clears the accumulator for the next SSE event.</summary>
    public void Reset() => _buffer.Clear();

    /// <summary>Current size of the in-progress event payload.</summary>
    public int BufferLength => _buffer.Length;

    /// <summary>
    /// Appends one line from the SSE stream.
    /// </summary>
    /// <param name="line">A single line without the line terminator.</param>
    /// <param name="exceededMemoryCap">True when <see cref="MaxBufferChars"/> was exceeded.</param>
    /// <returns>True when the event is complete (empty line or cap exceeded).</returns>
    public bool AppendLine(string line, out bool exceededMemoryCap)
    {
        exceededMemoryCap = false;

        // Blank line: end of event per SSE spec.
        if (line.Length == 0)
        {
            return true;
        }

        // Comment lines (e.g. ": ping") are heartbeats — ignore.
        if (line.StartsWith(":", StringComparison.Ordinal))
        {
            return false;
        }

        if (line.StartsWith("data:", StringComparison.Ordinal))
        {
            var payload = line.Length > 5 && line[5] == ' '
                ? line.Substring(6)
                : line.Substring(5);
            _buffer.Append(payload);

            if (_buffer.Length > MaxBufferChars)
            {
                exceededMemoryCap = true;
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the concatenated <c>data</c> payload and clears the buffer.</summary>
    public string TakePayload()
    {
        var result = _buffer.ToString();
        _buffer.Clear();
        return result;
    }
}
