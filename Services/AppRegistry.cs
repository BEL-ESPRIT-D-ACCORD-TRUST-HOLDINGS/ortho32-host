using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ortho32Host.Interfaces;
using Ortho32Host.IPC;
using Ortho32Host.Models;

namespace Ortho32Host.Services;

public class AppRegistry : IORTHOService
{
    public string Name => "app";
    private readonly ConcurrentDictionary<string, AppManifest> _apps = new();
    private readonly ILogger<AppRegistry> _logger;

    public AppRegistry(ILogger<AppRegistry> logger) { _logger = logger; }

    public Task StartAsync(CancellationToken ct) { _logger.LogInformation("AppRegistry started"); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { _logger.LogInformation("AppRegistry stopped"); return Task.CompletedTask; }

    public async IAsyncEnumerable<EventEnvelope> HandleStreamingAsync(MessageEnvelope envelope, [EnumeratorCancellation] CancellationToken ct)
    {
        if (envelope.Action == Router.RouteActions.APP_REGISTER)
        {
            AppManifest? manifest = null;
            try { manifest = JsonSerializer.Deserialize<AppManifest>(envelope.Payload.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); } catch { }
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.AppId))
            {
                yield return new EventEnvelope
                {
                    EventId = Guid.NewGuid().ToString(),
                    CorrelationId = envelope.CorrelationId,
                    EventType = "AppRegisterError",
                    Source = Name,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { error = "invalid manifest" })
                };
                yield break;
            }
            manifest.RegisteredAt = DateTime.UtcNow;
            _apps[manifest.AppId] = manifest;
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "AppRegistered",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(manifest)
            };
        }
        else if (envelope.Action == Router.RouteActions.APP_LIST)
        {
            var list = _apps.Values.ToArray();
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "AppList",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(list)
            };
        }
        else
        {
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "AppEvent",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(new { action = envelope.Action, count = _apps.Count })
            };
        }
        await Task.CompletedTask;
    }
}
