namespace Ortho32Host.Models;

public class BuildEvent
{
    public string CorrelationId { get; set; } = "";
    public string Stdout { get; set; } = "";
    public string Stderr { get; set; } = "";
    public int? ExitCode { get; set; }
    public string Phase { get; set; } = "stdout";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public bool Success => ExitCode == 0;
}
