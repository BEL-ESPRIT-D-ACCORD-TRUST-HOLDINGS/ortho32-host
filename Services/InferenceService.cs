using System.IO.Pipes;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ortho32Host.Interfaces;
using Ortho32Host.IPC;
using Ortho32Host.Models;

namespace Ortho32Host.Services;

public class InferenceService : IORTHOService
{
    public string Name => "inference";
    private readonly ILogger<InferenceService> _logger;

    public InferenceService(ILogger<InferenceService> logger) { _logger = logger; }

    public Task StartAsync(CancellationToken ct) { _logger.LogInformation("InferenceService started, will connect to ortho-ai pipe on demand"); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { _logger.LogInformation("InferenceService stopped"); return Task.CompletedTask; }

    public async IAsyncEnumerable<EventEnvelope> HandleStreamingAsync(MessageEnvelope envelope, [EnumeratorCancellation] CancellationToken ct)
    {
        var correlationId = string.IsNullOrWhiteSpace(envelope.CorrelationId) ? Guid.NewGuid().ToString() : envelope.CorrelationId;
        NamedPipeClientStream? client = null;
        try
        {
            client = new NamedPipeClientStream(".", "ortho-ai", PipeDirection.InOut, PipeOptions.Asynchronous);
        }
        catch (Exception ex)
        {
            yield return EventEnvelope.ServiceUnavailable(correlationId, Name, "ortho-ai pipe unavailable", ex.Message);
            yield break;
        }

        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectCts.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(connectCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ortho-ai pipe unavailable corr={Corr}", correlationId);
            yield return EventEnvelope.ServiceUnavailable(correlationId, Name, "ortho-ai pipe unavailable", ex.Message);
            try { client.Dispose(); } catch { }
            yield break;
        }

        using (client)
        using (var writer = new StreamWriter(client, leaveOpen: true) { AutoFlush = true })
        using (var reader = new StreamReader(client, leaveOpen: true))
        {
            var json = JsonSerializer.Serialize(envelope);
            try
            {
                await writer.WriteLineAsync(json.AsMemory(), ct);
                await writer.FlushAsync(ct);
            }
            catch (Exception ex)
            {
                yield return EventEnvelope.ServiceUnavailable(correlationId, Name, "failed to write to ortho-ai", ex.Message);
                yield break;
            }

            string? line;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var readTask = reader.ReadLineAsync(ct).AsTask();
                    var delayTask = Task.Delay(TimeSpan.FromSeconds(30), ct);
                    var done = await Task.WhenAny(readTask, delayTask);
                    if (done == delayTask) { _logger.LogWarning("Inference read timeout"); break; }
                    line = await readTask;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Read from ortho-ai failed");
                    break;
                }
                if (line == null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                InferenceEvent? ie = null;
                try { ie = JsonSerializer.Deserialize<InferenceEvent>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { }

                if (ie != null)
                {
                    ie.CorrelationId = correlationId;
                    var evtType = ie.Done ? "InferenceDone" : "InferenceChunk";
                    if (!string.IsNullOrEmpty(ie.Error)) evtType = "InferenceError";
                    yield return new EventEnvelope
                    {
                        EventId = Guid.NewGuid().ToString(),
                        CorrelationId = correlationId,
                        EventType = evtType,
                        Source = Name,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.SerializeToElement(ie)
                    };
                    if (ie.Done) break;
                }
                else
                {
                    yield return new EventEnvelope
                    {
                        EventId = Guid.NewGuid().ToString(),
                        CorrelationId = correlationId,
                        EventType = "InferenceChunk",
                        Source = Name,
                        Timestamp = DateTime.UtcNow,
                        Payload = JsonSerializer.SerializeToElement(new { raw = line, correlationId })
                    };
                }
            }
        }
    }
}
