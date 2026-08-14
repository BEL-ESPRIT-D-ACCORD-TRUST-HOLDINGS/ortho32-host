using System.Collections.Concurrent;
using System.Threading.Channels;
using Ortho32Host.IPC;

namespace Ortho32Host.EventBus;

public class ORTHOEventBus
{
    private readonly ConcurrentDictionary<string, Channel<EventEnvelope>> _channels = new();
    private readonly ConcurrentDictionary<string, List<Action<EventEnvelope>>> _subscribers = new();
    private readonly ConcurrentDictionary<string, List<EventEnvelope>> _history = new();

    public void Publish(EventEnvelope ev)
    {
        var corr = ev.CorrelationId ?? "*";
        var hist = _history.GetOrAdd(corr, _ => new List<EventEnvelope>());
        lock (hist) hist.Add(ev);

        if (_channels.TryGetValue(corr, out var ch))
            ch.Writer.TryWrite(ev);
        if (_channels.TryGetValue("*", out var globalCh))
            globalCh.Writer.TryWrite(ev);

        if (_subscribers.TryGetValue(corr, out var subs))
        {
            Action<EventEnvelope>[] copy;
            lock (subs) copy = subs.ToArray();
            foreach (var a in copy) try { a(ev); } catch { }
        }
        if (_subscribers.TryGetValue("*", out var gsubs))
        {
            Action<EventEnvelope>[] copy;
            lock (gsubs) copy = gsubs.ToArray();
            foreach (var a in copy) try { a(ev); } catch { }
        }
    }

    public Channel<EventEnvelope> GetOrCreateChannel(string correlationId)
    {
        return _channels.GetOrAdd(correlationId, _ => Channel.CreateUnbounded<EventEnvelope>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false }));
    }

    public IDisposable Subscribe(string correlationId, Action<EventEnvelope> handler)
    {
        var key = string.IsNullOrEmpty(correlationId) ? "*" : correlationId;
        var list = _subscribers.GetOrAdd(key, _ => new List<Action<EventEnvelope>>());
        lock (list) list.Add(handler);
        return new Subscription(() => { lock (list) list.Remove(handler); });
    }

    public IAsyncEnumerable<EventEnvelope> StreamAsync(string correlationId, CancellationToken ct)
    {
        var ch = GetOrCreateChannel(correlationId);
        return ch.Reader.ReadAllAsync(ct);
    }

    public IReadOnlyList<EventEnvelope> GetHistory(string correlationId)
    {
        if (_history.TryGetValue(correlationId, out var list)) lock (list) return list.ToArray();
        return Array.Empty<EventEnvelope>();
    }

    public void Complete(string correlationId)
    {
        if (_channels.TryGetValue(correlationId, out var ch)) ch.Writer.TryComplete();
    }

    private class Subscription : IDisposable
    {
        private readonly Action _dispose;
        private bool _disposed;
        public Subscription(Action dispose) => _dispose = dispose;
        public void Dispose() { if (!_disposed) { _dispose(); _disposed = true; } }
    }
}
