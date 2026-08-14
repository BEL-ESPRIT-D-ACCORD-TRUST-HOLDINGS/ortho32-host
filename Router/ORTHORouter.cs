using Microsoft.Extensions.Logging;
using Ortho32Host.IPC;
using Ortho32Host.Services;
using Ortho32Host.EventBus;

namespace Ortho32Host.Router;

public class ORTHORouter
{
    private readonly ServiceRegistry _registry;
    private readonly CapabilityBroker _broker;
    private readonly ORTHOEventBus _bus;
    private readonly ILogger<ORTHORouter> _logger;

    public ORTHORouter(ServiceRegistry registry, CapabilityBroker broker, ORTHOEventBus bus, ILogger<ORTHORouter> logger)
    {
        _registry = registry;
        _broker = broker;
        _bus = bus;
        _logger = logger;
    }

    public async Task RouteAsync(MessageEnvelope msg, Func<EventEnvelope, Task> emit, CancellationToken ct)
    {
        if (!_broker.Authorize(msg))
        {
            var unauth = new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = msg.CorrelationId,
                EventType = "Unauthorized",
                Source = "router",
                Timestamp = DateTime.UtcNow,
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { error = "capability denied", action = msg.Action })
            };
            _bus.Publish(unauth);
            await emit(unauth);
            return;
        }

        var svc = Resolve(msg);
        if (svc == null)
        {
            if (msg.Action == RouteActions.PING)
            {
                var pong = new EventEnvelope
                {
                    EventId = Guid.NewGuid().ToString(),
                    CorrelationId = msg.CorrelationId,
                    EventType = "Pong",
                    Source = "router",
                    Timestamp = DateTime.UtcNow,
                    Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { pong = true, action = msg.Action })
                };
                await emit(pong);
                return;
            }
            var unknown = new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = msg.CorrelationId,
                EventType = "UnknownTarget",
                Source = "router",
                Timestamp = DateTime.UtcNow,
                Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { error = $"no service for action {msg.Action} target {msg.Target}" })
            };
            await emit(unknown);
            return;
        }

        _logger.LogInformation("Dispatch {Action} to {Service} corr={Corr}", msg.Action, svc.Name, msg.CorrelationId);
        await foreach (var ev in svc.HandleStreamingAsync(msg, ct))
        {
            _bus.Publish(ev);
            await emit(ev);
            if (ct.IsCancellationRequested) break;
        }
    }

    private Interfaces.IORTHOService? Resolve(MessageEnvelope msg)
    {
        if (!string.IsNullOrWhiteSpace(msg.Target))
        {
            var byTarget = _registry.Resolve(msg.Target);
            if (byTarget != null) return byTarget;
        }
        return msg.Action switch
        {
            RouteActions.INFERENCE_SUBMIT => _registry.Resolve("inference"),
            RouteActions.INFERENCE_STREAM => _registry.Resolve("inference"),
            RouteActions.BUILD_RUN => _registry.Resolve("build"),
            RouteActions.BUILD_CANCEL => _registry.Resolve("build"),
            RouteActions.DEVICE_COMMAND => _registry.Resolve("device"),
            RouteActions.DEVICE_POLL => _registry.Resolve("device"),
            RouteActions.SESSION_CREATE => _registry.Resolve("session"),
            RouteActions.SESSION_GET => _registry.Resolve("session"),
            RouteActions.APP_REGISTER => _registry.Resolve("app"),
            RouteActions.APP_LIST => _registry.Resolve("app"),
            _ => null
        };
    }
}
