using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Channels;

namespace BattleServer;

public sealed class BattleLogStore
{
    private const int MaxEntries = 2000;
    private readonly object _lock = new();
    private readonly List<BattleLogEntry> _entries = new();
    private readonly ConcurrentDictionary<Guid, Channel<BattleLogEntry>> _subscribers = new();
    private long _nextId;

    public BattleLogEntry Append(string rawMessage, bool isError = false)
    {
        string normalized = Normalize(rawMessage);
        var entry = CreateEntry(normalized, isError);

        List<Channel<BattleLogEntry>> subscribers;
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(0, _entries.Count - MaxEntries);
            subscribers = _subscribers.Values.ToList();
        }

        foreach (var channel in subscribers)
            channel.Writer.TryWrite(entry);

        return entry;
    }

    public IReadOnlyList<BattleLogEntry> GetRecent(int take)
    {
        lock (_lock)
        {
            int safeTake = Math.Clamp(take, 1, MaxEntries);
            int skip = Math.Max(0, _entries.Count - safeTake);
            return _entries.Skip(skip).ToArray();
        }
    }

    public (Guid subscriptionId, ChannelReader<BattleLogEntry> reader) Subscribe()
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<BattleLogEntry>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _subscribers[id] = channel;
        return (id, channel.Reader);
    }

    public void Unsubscribe(Guid subscriptionId)
    {
        if (_subscribers.TryRemove(subscriptionId, out var channel))
            channel.Writer.TryComplete();
    }

    private BattleLogEntry CreateEntry(string raw, bool isError)
    {
        string tag = DetectTag(raw);
        string level = DetectLevel(raw, isError);
        string message = StripLeadingTag(raw);
        return new BattleLogEntry
        {
            Id = Interlocked.Increment(ref _nextId),
            Utc = DateTimeOffset.UtcNow,
            Level = level,
            Tag = tag,
            Message = message,
            Raw = raw
        };
    }

    private static string Normalize(string rawMessage)
    {
        if (string.IsNullOrWhiteSpace(rawMessage))
            return string.Empty;
        return rawMessage.Replace("\r\n", "\n").Trim();
    }

    private static string DetectTag(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "system";

        int open = raw.IndexOf('[');
        int close = raw.IndexOf(']');
        if (open >= 0 && close > open)
        {
            string candidate = raw[(open + 1)..close].Trim();
            if (!string.IsNullOrEmpty(candidate) && candidate.All(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_'))
                return candidate;
        }

        if (raw.Contains(" GET ", StringComparison.Ordinal) ||
            raw.Contains(" POST ", StringComparison.Ordinal) ||
            raw.Contains(" PUT ", StringComparison.Ordinal) ||
            raw.Contains(" DELETE ", StringComparison.Ordinal))
            return "http";

        return "system";
    }

    private static string DetectLevel(string raw, bool isError)
    {
        if (isError)
            return "error";
        if (raw.Contains(" error", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains(" failed", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("exception", StringComparison.OrdinalIgnoreCase))
            return "error";
        if (raw.Contains("warn", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains(" reject", StringComparison.OrdinalIgnoreCase))
            return "warn";
        return "info";
    }

    private static string StripLeadingTag(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        int close = raw.IndexOf(']');
        if (raw.StartsWith("[", StringComparison.Ordinal) && close >= 0 && close + 1 < raw.Length)
            return raw[(close + 1)..].TrimStart();

        return raw;
    }
}

public sealed class BattleLogEntry
{
    public long Id { get; init; }
    public DateTimeOffset Utc { get; init; }
    public string Level { get; init; } = "info";
    public string Tag { get; init; } = "system";
    public string Message { get; init; } = "";
    public string Raw { get; init; } = "";
}

public sealed class BattleLogConsoleWriter : TextWriter
{
    private readonly TextWriter _inner;
    private readonly BattleLogStore _store;
    private readonly bool _isError;
    private readonly StringBuilder _buffer = new();
    private readonly object _sync = new();

    public BattleLogConsoleWriter(TextWriter inner, BattleLogStore store, bool isError)
    {
        _inner = inner;
        _store = store;
        _isError = isError;
    }

    public override Encoding Encoding => _inner.Encoding;

    public override void Write(char value)
    {
        lock (_sync)
        {
            _inner.Write(value);
            if (value == '\n')
            {
                FlushBuffer();
                return;
            }

            if (value != '\r')
                _buffer.Append(value);
        }
    }

    public override void Write(string? value)
    {
        if (value == null)
            return;

        lock (_sync)
        {
            _inner.Write(value);
            foreach (char ch in value)
            {
                if (ch == '\n')
                    FlushBuffer();
                else if (ch != '\r')
                    _buffer.Append(ch);
            }
        }
    }

    public override void WriteLine(string? value)
    {
        lock (_sync)
        {
            _inner.WriteLine(value);
            if (!string.IsNullOrEmpty(value))
                _buffer.Append(value);
            FlushBuffer();
        }
    }

    public override void Flush()
    {
        lock (_sync)
        {
            _inner.Flush();
            if (_buffer.Length > 0)
                FlushBuffer();
        }
    }

    private void FlushBuffer()
    {
        string text = _buffer.ToString();
        _buffer.Clear();
        if (!string.IsNullOrWhiteSpace(text))
            _store.Append(text, _isError);
    }
}
