namespace Ortho32Host.Models;

public class Session
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccess { get; set; } = DateTime.UtcNow;
    public string UserId { get; set; } = "system";
    public Dictionary<string, string> Metadata { get; set; } = new();
    public Dictionary<string, object> CapabilityContext { get; set; } = new();
    public string Status { get; set; } = "active";
}
