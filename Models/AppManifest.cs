namespace Ortho32Host.Models;

public class AppManifest
{
    public string AppId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "1.0.0";
    public string EntryPoint { get; set; } = "";
    public List<string> Capabilities { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
    public DateTime RegisteredAt { get; set; } = DateTime.UtcNow;
}
