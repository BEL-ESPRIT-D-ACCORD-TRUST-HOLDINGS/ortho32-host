using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ortho32Host.IPC;
using Ortho32Host.Services;

namespace Ortho32Host.Workers;

public class ORTHOHostWorker : BackgroundService
{
    private readonly NamedPipeServer _pipeServer;
    private readonly ServiceRegistry _registry;
    private readonly ILogger<ORTHOHostWorker> _logger;

    public ORTHOHostWorker(NamedPipeServer pipeServer, ServiceRegistry registry, ILogger<ORTHOHostWorker> logger)
    {
        _pipeServer = pipeServer;
        _registry = registry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ORTHOHostWorker starting - ORTHO32 Integration Recovery spine active");
        try
        {
            await _registry.StartAllAsync(stoppingToken);
            await _pipeServer.StartAsync(stoppingToken);
            _logger.LogInformation("ORTHOHost ready on pipe ortho-host. Router, EventBus, Services online.");

            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                _logger.LogDebug("ORTHOHost heartbeat - services: {Count}", _registry.All.Count());
            }
        }
        catch (OperationCanceledException) { _logger.LogInformation("ORTHOHostWorker cancellation requested"); }
        catch (Exception ex) { _logger.LogCritical(ex, "ORTHOHostWorker fatal error"); throw; }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ORTHOHostWorker stopping");
        try { await _pipeServer.StopAsync(cancellationToken); } catch (Exception ex) { _logger.LogError(ex, "Error stopping pipe server"); }
        try { await _registry.StopAllAsync(); } catch (Exception ex) { _logger.LogError(ex, "Error stopping services"); }
        await base.StopAsync(cancellationToken);
        _logger.LogInformation("ORTHOHostWorker stopped");
    }
}
