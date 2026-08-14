using System.Text.Json;

namespace Ortho32Host.IPC;

public class MessageEnvelope
{
    public string RequestId { get; set; } = Guid.NewGuid().ToString();
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString();
    public string Source { get; set; } = "";
    public string Target { get; set; } = "";
    public string Action { get; set; } = "";
    public JsonElement Payload { get; set; }
    public JsonElement CapabilityContext { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public static MessageEnvelope Create(string action, object? payload, string? correlationId = null, string source = "client", string target = "")
    {
        var env = new MessageEnvelope
        {
            Action = action,
            Source = source,
            Target = target,
            CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
            RequestId = Guid.NewGuid().ToString(),
            Timestamp = DateTime.UtcNow
        };
        if (payload != null)
            env.Payload = JsonSerializer.SerializeToElement(payload);
        else
            env.Payload = JsonDocument.Parse("{}").RootElement.Clone();
        env.CapabilityContext = JsonDocument.Parse("{}").RootElement.Clone();
        return env;
    }
}
