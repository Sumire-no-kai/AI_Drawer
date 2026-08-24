using System.Text;

namespace AIDock.CompatibilityLab;

/// <summary>
/// Debug-only, bounded diagnostics for compatibility investigations. It never writes to disk or the network.
/// </summary>
internal sealed class SanitizedDiagnosticLog
{
    private const int MaximumEventCount = 100;
    private readonly Queue<string> _events = [];

    internal void Record(string eventCode)
    {
#if DEBUG
        if (string.IsNullOrWhiteSpace(eventCode)
            || eventCode.Any(character => !(char.IsAsciiLetterOrDigit(character)
                || character is ' ' or '=' or '-' or '_')))
        {
            return;
        }

        while (_events.Count >= MaximumEventCount)
        {
            _events.Dequeue();
        }

        _events.Enqueue($"[{DateTimeOffset.Now:HH:mm:ss}] {eventCode}");
#endif
    }

    internal void Clear() => _events.Clear();

    internal string Render()
    {
#if DEBUG
        return string.Join(Environment.NewLine, _events);
#else
        return string.Empty;
#endif
    }
}
