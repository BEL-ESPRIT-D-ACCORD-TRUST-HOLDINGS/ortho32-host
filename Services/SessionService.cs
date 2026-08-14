using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ortho32Host.Interfaces;
using Ortho32Host.IPC;
using Ortho32Host.Models;
using Ortho32Host.Router;

namespace Ortho32Host.Services;

public class SessionService : IORTHOService
{
    public string Name => "session";
    private readonly ConcurrentDictionary<string, Session> _sessions = new();
    private readonly ILogger<SessionService> _logger;

    public SessionService(ILogger<SessionService> logger) { _logger = logger; }

    public Task StartAsync(CancellationToken ct) { _logger.LogInformation("SessionService started"); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { _logger.LogInformation("SessionService stopped"); return Task.CompletedTask; }

    public async IAsyncEnumerable<EventEnvelope> HandleStreamingAsync(MessageEnvelope envelope, [EnumeratorCancellation] CancellationToken ct)
    {
        if (envelope.Action == RouteActions.SESSION_CREATE)
        {
            var session = new Session { Id = Guid.NewGuid().ToString(), CreatedAt = DateTime.UtcNow, LastAccess = DateTime.UtcNow };
            try
            {
                if (envelope.Payload.ValueKind == JsonValueKind.Object && envelope.Payload.TryGetProperty("userId", out var uid))
                    session.UserId = uid.GetString() ?? "system";
                if (envelope.Payload.ValueKind == JsonValueKind.Object && envelope.Payload.TryGetProperty("metadata", out var meta) && meta.ValueKind == JsonValueKind.Object)
                    foreach (var p in meta.EnumerateObject()) session.Metadata[p.Name] = p.Value.GetString() ?? "";
            }
            catch { }
            _sessions[session.Id] = session;
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "SessionCreated",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(session)
            };
        }
        else if (envelope.Action == RouteActions.SESSION_GET)
        {
            string sid = "";
            try { if (envelope.Payload.TryGetProperty("sessionId", out var s)) sid = s.GetString() ?? ""; } catch { }
            if (!string.IsNullOrEmpty(sid) && _sessions.TryGetValue(sid, out var sess))
            {
                sess.LastAccess = DateTime.UtcNow;
                yield return new EventEnvelope
                {
                    EventId = Guid.NewGuid().ToString(),
                    CorrelationId = envelope.CorrelationId,
                    EventType = "SessionFound",
                    Source = Name,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(sess)
                };
            }
            else
            {
                yield return new EventEnvelope
                {
                    EventId = Guid.NewGuid().ToString(),
                    CorrelationId = envelope.CorrelationId,
                    EventType = "SessionNotFound",
                    Source = Name,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { sessionId = sid, error = "not found" })
                };
            }
        }
        else
        {
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "SessionEvent",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(new { action = envelope.Action, sessions = _sessions.Count })
            };
        }
        await Task.CompletedTask;
    }

    public Session? Get(string id) => _sessions.TryGetValue(id, out var s) ? s : null;
    public IReadOnlyCollection<Session> All => _sessions.Values.ToArray();
}
