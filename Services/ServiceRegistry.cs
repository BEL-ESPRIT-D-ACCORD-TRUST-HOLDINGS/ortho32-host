using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Ortho32Host.Interfaces;

namespace Ortho32Host.Services;

public class ServiceRegistry
{
    private readonly ConcurrentDictionary<string, IORTHOService> _services = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ServiceRegistry> _logger;

    public ServiceRegistry(ILogger<ServiceRegistry> logger) { _logger = logger; }

    public void Register(IORTHOService service)
    {
        _services[service.Name] = service;
        _logger.LogInformation("Registered service {Name}", service.Name);
    }

    public IORTHOService? Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        _services.TryGetValue(name, out var svc);
        return svc;
    }

    public IEnumerable<IORTHOService> All => _services.Values;

    public async Task StartAllAsync(CancellationToken ct)
    {
        foreach (var kv in _services)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _running[kv.Key] = cts;
            try
            {
                await kv.Value.StartAsync(cts.Token);
                _logger.LogInformation("Service {Name} started", kv.Key);
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed to start service {Name}", kv.Key); }
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var kv in _running) try { kv.Value.Cancel(); } catch { }
        foreach (var kv in _services)
        {
            try { await kv.Value.StopAsync(CancellationToken.None); _logger.LogInformation("Service {Name} stopped", kv.Key); }
            catch (Exception ex) { _logger.LogError(ex, "Error stopping {Name}", kv.Key); }
        }
        foreach (var kv in _running) try { kv.Value.Dispose(); } catch { }
        _running.Clear();
    }

    public async Task RestartAsync(string name)
    {
        if (!_services.TryGetValue(name, out var svc)) throw new InvalidOperationException($"Service {name} not found");
        _logger.LogInformation("Restarting service {Name}", name);
        if (_running.TryRemove(name, out var oldCts))
        {
            try { oldCts.Cancel(); } catch { }
            try { await svc.StopAsync(CancellationToken.None); } catch (Exception ex) { _logger.LogError(ex, "Error stopping {Name} during restart", name); }
            try { oldCts.Dispose(); } catch { }
        }
        var newCts = new CancellationTokenSource();
        _running[name] = newCts;
        await svc.StartAsync(newCts.Token);
        _logger.LogInformation("Service {Name} restarted successfully", name);
    }
}
