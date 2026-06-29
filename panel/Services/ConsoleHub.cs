using System.Collections.Concurrent;
using System.Threading.Channels;

namespace TribesServerPanel.Services;

/// <summary>Ring buffer of console lines + fan-out to live SSE subscribers.</summary>
public sealed class ConsoleHub
{
    private readonly int _ringSize;
    private readonly LinkedList<string> _ring = new();
    private readonly object _lock = new();
    private readonly ConcurrentDictionary<Guid, Channel<string>> _subs = new();

    public ConsoleHub(IConfiguration cfg) => _ringSize = cfg.GetValue("CONSOLE_RING", 1000);

    public void Publish(string line)
    {
        lock (_lock)
        {
            _ring.AddLast(line);
            while (_ring.Count > _ringSize) _ring.RemoveFirst();
        }
        foreach (var ch in _subs.Values)
            ch.Writer.TryWrite(line); // bounded+drop-oldest: never blocks the producer
    }

    public IReadOnlyList<string> Snapshot(int n)
    {
        lock (_lock)
        {
            var arr = _ring.ToArray();
            return n > 0 && n < arr.Length ? arr[^n..] : arr;
        }
    }

    public (Guid id, ChannelReader<string> reader) Subscribe()
    {
        var ch = Channel.CreateBounded<string>(new BoundedChannelOptions(2000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
        });
        var id = Guid.NewGuid();
        _subs[id] = ch;
        return (id, ch.Reader);
    }

    public void Unsubscribe(Guid id)
    {
        if (_subs.TryRemove(id, out var ch)) ch.Writer.TryComplete();
    }
}
