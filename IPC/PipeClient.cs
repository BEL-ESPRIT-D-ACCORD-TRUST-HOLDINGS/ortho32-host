using System.IO.Pipes;
using System.Text.Json;

namespace Ortho32Host.IPC;

public class PipeClient : IAsyncDisposable
{
    private NamedPipeClientStream? _client;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private readonly string _pipeName;
    private readonly string _serverName;

    public PipeClient(string pipeName = "ortho-host", string serverName = ".")
    {
        _pipeName = pipeName;
        _serverName = serverName;
    }

    public async Task ConnectAsync(int timeoutMs = 5000, CancellationToken ct = default)
    {
        _client = new NamedPipeClientStream(_serverName, _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);
        await _client.ConnectAsync(cts.Token);
        _reader = new StreamReader(_client, leaveOpen: true);
        _writer = new StreamWriter(_client, leaveOpen: true) { AutoFlush = true };
    }

    public async Task SendAsync(MessageEnvelope envelope, CancellationToken ct = default)
    {
        if (_writer == null) throw new InvalidOperationException("Not connected");
        var json = JsonSerializer.Serialize(envelope);
        await _writer.WriteLineAsync(json.AsMemory(), ct);
        await _writer.FlushAsync(ct);
    }

    public async Task<EventEnvelope?> ReadEventAsync(CancellationToken ct = default)
    {
        if (_reader == null) throw new InvalidOperationException("Not connected");
        string? line = await _reader.ReadLineAsync(ct);
        if (line == null) return null;
        if (string.IsNullOrWhiteSpace(line)) return null;
        return JsonSerializer.Deserialize<EventEnvelope>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    public async Task<List<EventEnvelope>> ReadEventsUntilCompletedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        var list = new List<EventEnvelope>();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                var evTask = ReadEventAsync(cts.Token);
                var completed = await Task.WhenAny(evTask, Task.Delay(50, cts.Token));
                if (evTask.IsCompleted && await evTask != null)
                {
                    var ev = await evTask;
                    if (ev != null)
                    {
                        list.Add(ev);
                        if (ev.EventType == "StreamCompleted" || ev.EventType == "BuildCompleted" || ev.EventType == "InferenceDone") break;
                    }
                }
                if (cts.Token.IsCancellationRequested) break;
                await Task.Delay(10, cts.Token);
            }
        }
        catch (OperationCanceledException) { }
        return list;
    }

    public async ValueTask DisposeAsync()
    {
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        try { _writer?.Dispose(); } catch { }
        try { _reader?.Dispose(); } catch { }
        try { _client?.Dispose(); } catch { }
    }
}
