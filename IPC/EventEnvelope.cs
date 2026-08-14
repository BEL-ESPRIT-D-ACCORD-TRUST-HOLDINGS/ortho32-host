using System.Text.Json;

namespace Ortho32Host.IPC;

public class EventEnvelope
{
    public string EventId { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = "";
    public string EventType { get; set; } = "";
    public string Source { get; set; } = "";
    public JsonElement Payload { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static EventEnvelope ServiceUnavailable(string correlationId, string source, string error, string details = "")
    {
        return new EventEnvelope
        {
            EventId = Guid.NewGuid().ToString(),
            CorrelationId = correlationId,
            EventType = "ServiceUnavailableEvent",
            Source = source,
            Timestamp = DateTime.UtcNow,
            Payload = JsonSerializer.SerializeToElement(new { error, details, service = source, timestamp = DateTime.UtcNow })
        };
    }
}
