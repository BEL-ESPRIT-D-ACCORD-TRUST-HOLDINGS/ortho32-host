using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ortho32Host.IPC;

namespace Ortho32Host.Services;

public class CapabilityBroker
{
    private readonly ILogger<CapabilityBroker> _logger;
    private readonly HashSet<string> _restrictedActions = new(StringComparer.OrdinalIgnoreCase) { "DEVICE_COMMAND", "BUILD_RUN" };

    public CapabilityBroker(ILogger<CapabilityBroker> logger) { _logger = logger; }

    public bool Authorize(MessageEnvelope envelope)
    {
        if (envelope.CapabilityContext.ValueKind == JsonValueKind.Undefined || envelope.CapabilityContext.ValueKind == JsonValueKind.Null) return true;
        try
        {
            if (envelope.CapabilityContext.ValueKind == JsonValueKind.Object)
            {
                if (envelope.CapabilityContext.TryGetProperty("deny", out var deny) && deny.ValueKind == JsonValueKind.Array)
                {
                    foreach (var d in deny.EnumerateArray())
                        if (d.GetString()?.Equals(envelope.Action, StringComparison.OrdinalIgnoreCase) == true) return false;
                }
                if (envelope.CapabilityContext.TryGetProperty("allow", out var allow) && allow.ValueKind == JsonValueKind.Array)
                {
                    var allowed = allow.EnumerateArray().Select(x => x.GetString()).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
                    if (allowed.Count > 0 && !allowed.Contains(envelope.Action) && !allowed.Contains("*")) return false;
                }
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Capability check failed, allowing"); }
        return true;
    }

    public bool HasCapability(JsonElement context, string capability)
    {
        try
        {
            if (context.ValueKind == JsonValueKind.Object && context.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
                foreach (var c in caps.EnumerateArray()) if (c.GetString() == capability) return true;
        }
        catch { }
        return false;
    }
}
