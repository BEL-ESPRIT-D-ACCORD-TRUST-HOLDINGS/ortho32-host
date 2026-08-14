namespace Ortho32Host.Models;

public class InferenceEvent
{
    public string CorrelationId { get; set; } = "";
    public string Chunk { get; set; } = "";
    public bool Done { get; set; }
    public string Model { get; set; } = "";
    public int Tokens { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? Error { get; set; }
}
