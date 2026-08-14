using System.IO.Pipes;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ortho32Host.Router;
using Ortho32Host.EventBus;

namespace Ortho32Host.IPC;

public class NamedPipeServer
{
    private readonly ORTHORouter _router;
    private readonly ORTHOEventBus _bus;
    private readonly ILogger<NamedPipeServer> _logger;
    private CancellationTokenSource? _cts;
    private Task? _listenTask;

    public NamedPipeServer(ORTHORouter router, ORTHOEventBus bus, ILogger<NamedPipeServer> logger)
    {
        _router = router;
        _bus = bus;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken ct)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _listenTask = Task.Run(() => ListenLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("NamedPipeServer started on pipe ortho-host");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken ct)
    {
        try { _cts?.Cancel(); } catch { }
        if (_listenTask != null)
        {
            try { await _listenTask.WaitAsync(TimeSpan.FromSeconds(2), ct); } catch { }
        }
        _logger.LogInformation("NamedPipeServer stopped");
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var server = new NamedPipeServerStream("ortho-host", PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            try
            {
                await server.WaitForConnectionAsync(ct);
                _ = Task.Run(() => HandleClientAsync(server, ct), ct);
            }
            catch (OperationCanceledException) { try { server.Dispose(); } catch { } break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ListenLoop error");
                try { server.Dispose(); } catch { }
                await Task.Delay(500, ct);
            }
        }
    }

    private async Task HandleClientAsync(NamedPipeServerStream stream, CancellationToken ct)
    {
        var clientId = Guid.NewGuid().ToString()[..8];
        _logger.LogInformation("Client connected {ClientId}", clientId);
        using (stream)
        using (var reader = new StreamReader(stream, leaveOpen: true))
        using (var writer = new StreamWriter(stream, leaveOpen: true) { AutoFlush = true })
        {
            try
            {
                while (!ct.IsCancellationRequested && stream.IsConnected)
                {
                    string? line = null;
                    try
                    {
                        var readTask = reader.ReadLineAsync(ct).AsTask();
                        var completed = await Task.WhenAny(readTask, Task.Delay(Timeout.Infinite, ct));
                        if (readTask.IsCompleted) line = await readTask;
                        else break;
                    }
                    catch (OperationCanceledException) { break; }
                    catch (IOException) { break; }

                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    MessageEnvelope? env = null;
                    try
                    {
                        env = JsonSerializer.Deserialize<MessageEnvelope>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to deserialize envelope: {Line}", line);
                        var err = new EventEnvelope
                        {
                            EventId = Guid.NewGuid().ToString(),
                            CorrelationId = "",
                            EventType = "ProtocolError",
                            Source = "host",
                            Timestamp = DateTime.UtcNow,
                            Payload = JsonSerializer.SerializeToElement(new { error = "invalid json", details = ex.Message })
                        };
                        await writer.WriteLineAsync(JsonSerializer.Serialize(err));
                        continue;
                    }
                    if (env == null) continue;
                    if (string.IsNullOrEmpty(env.CorrelationId)) env.CorrelationId = Guid.NewGuid().ToString();
                    if (string.IsNullOrEmpty(env.RequestId)) env.RequestId = Guid.NewGuid().ToString();

                    _logger.LogInformation("Routing {Action} corr={Corr} from {Source}", env.Action, env.CorrelationId, env.Source);

                    Func<EventEnvelope, Task> emit = async (e) =>
                    {
                        try
                        {
                            var json = JsonSerializer.Serialize(e);
                            await writer.WriteLineAsync(json);
                            await writer.FlushAsync();
                        }
                        catch (Exception ex) { _logger.LogError(ex, "Emit failed"); }
                    };

                    try
                    {
                        await _router.RouteAsync(env, emit, ct);
                        var completion = new EventEnvelope
                        {
                            EventId = Guid.NewGuid().ToString(),
                            CorrelationId = env.CorrelationId,
                            EventType = "StreamCompleted",
                            Source = "host",
                            Timestamp = DateTime.UtcNow,
                            Payload = JsonSerializer.SerializeToElement(new { correlationId = env.CorrelationId, completed = true })
                        };
                        await emit(completion);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Route failed for {Action}", env.Action);
                        var err = new EventEnvelope
                        {
                            EventId = Guid.NewGuid().ToString(),
                            CorrelationId = env.CorrelationId,
                            EventType = "RouterError",
                            Source = "host",
                            Timestamp = DateTime.UtcNow,
                            Payload = JsonSerializer.SerializeToElement(new { error = ex.Message })
                        };
                        await emit(err);
                    }
                }
            }
            catch (Exception ex) { _logger.LogError(ex, "HandleClient error {ClientId}", clientId); }
            finally { _logger.LogInformation("Client disconnected {ClientId}", clientId); try { if (stream.IsConnected) stream.Disconnect(); } catch { } }
        }
    }
}
