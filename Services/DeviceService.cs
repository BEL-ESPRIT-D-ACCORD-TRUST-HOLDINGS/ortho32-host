using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ortho32Host.Interfaces;
using Ortho32Host.IPC;

namespace Ortho32Host.Services;

public class DeviceService : IORTHOService
{
    public string Name => "device";
    private readonly ILogger<DeviceService> _logger;
    private IntPtr _libHandle = IntPtr.Zero;
    private bool _bridgeAvailable = false;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int OrthoBridgeSendDelegate([MarshalAs(UnmanagedType.LPStr)] string payload, [MarshalAs(UnmanagedType.LPStr)] string correlationId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr OrthoBridgePollDelegate([MarshalAs(UnmanagedType.LPStr)] string correlationId);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr OrthoBridgeVersionDelegate();

    public DeviceService(ILogger<DeviceService> logger) { _logger = logger; }

    public Task StartAsync(CancellationToken ct)
    {
        try
        {
            string[] candidates = ["ORTHOBridge.dll", "ORTHOBridge", "libORTHOBridge.so", "libORTHOBridge.dylib"];
            foreach (var name in candidates)
            {
                if (NativeLibrary.TryLoad(name, out var handle))
                {
                    _libHandle = handle;
                    _bridgeAvailable = true;
                    _logger.LogInformation("ORTHOBridge loaded from {Name} handle=0x{Handle:X}", name, handle.ToInt64());
                    if (NativeLibrary.TryGetExport(handle, "ortho_bridge_version", out var verPtr))
                    {
                        try
                        {
                            var verDel = Marshal.GetDelegateForFunctionPointer<OrthoBridgeVersionDelegate>(verPtr);
                            var verPtrStr = verDel();
                            var ver = Marshal.PtrToStringAnsi(verPtrStr) ?? "unknown";
                            _logger.LogInformation("ORTHOBridge version {Version}", ver);
                        }
                        catch (Exception ex) { _logger.LogWarning(ex, "Failed to get bridge version"); }
                    }
                    break;
                }
            }
            if (!_bridgeAvailable) _logger.LogWarning("ORTHOBridge C ABI not found - DeviceService will return ServiceUnavailableEvent");
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to load ORTHOBridge"); _bridgeAvailable = false; }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        if (_libHandle != IntPtr.Zero)
        {
            try { NativeLibrary.Free(_libHandle); _logger.LogInformation("ORTHOBridge freed"); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to free ORTHOBridge"); }
            _libHandle = IntPtr.Zero;
        }
        _bridgeAvailable = false;
        return Task.CompletedTask;
    }

    public async IAsyncEnumerable<EventEnvelope> HandleStreamingAsync(MessageEnvelope envelope, [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_bridgeAvailable || _libHandle == IntPtr.Zero)
        {
            yield return EventEnvelope.ServiceUnavailable(envelope.CorrelationId, Name, "ORTHOBridge C ABI not available", "ORTHOBridge.dll missing - hardware bridge not installed");
            yield break;
        }

        string payloadJson = envelope.Payload.ValueKind != JsonValueKind.Undefined ? envelope.Payload.GetRawText() : "{}";

        if (!NativeLibrary.TryGetExport(_libHandle, "ortho_bridge_send", out var sendPtr))
        {
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "ServiceUnavailableEvent",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(new { error = "ORTHOBridge symbol ortho_bridge_send not found", bridge = "ORTHOBridge.dll", symbol = "ortho_bridge_send" })
            };
            yield break;
        }

        int sendResult = -1;
        try
        {
            var sendDel = Marshal.GetDelegateForFunctionPointer<OrthoBridgeSendDelegate>(sendPtr);
            sendResult = sendDel(payloadJson, envelope.CorrelationId);
            _logger.LogInformation("ortho_bridge_send returned {Result} corr={Corr}", sendResult, envelope.CorrelationId);
        }
        catch (Exception ex)
        {
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "ServiceUnavailableEvent",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(new { error = ex.Message, phase = "send" })
            };
            yield break;
        }

        yield return new EventEnvelope
        {
            EventId = Guid.NewGuid().ToString(),
            CorrelationId = envelope.CorrelationId,
            EventType = "DeviceAck",
            Source = Name,
            Timestamp = DateTime.UtcNow,
            Payload = JsonSerializer.SerializeToElement(new { status = "sent", result = sendResult, correlationId = envelope.CorrelationId })
        };

        if (NativeLibrary.TryGetExport(_libHandle, "ortho_bridge_poll", out var pollPtr))
        {
            var pollDel = Marshal.GetDelegateForFunctionPointer<OrthoBridgePollDelegate>(pollPtr);
            for (int i = 0; i < 100 && !ct.IsCancellationRequested; i++)
            {
                IntPtr completionPtr = IntPtr.Zero;
                try { completionPtr = pollDel(envelope.CorrelationId); } catch (Exception ex) { _logger.LogError(ex, "poll failed"); break; }
                if (completionPtr != IntPtr.Zero)
                {
                    string? completion = Marshal.PtrToStringAnsi(completionPtr);
                    if (!string.IsNullOrEmpty(completion))
                    {
                        yield return new EventEnvelope
                        {
                            EventId = Guid.NewGuid().ToString(),
                            CorrelationId = envelope.CorrelationId,
                            EventType = "DeviceCompletion",
                            Source = Name,
                            Timestamp = DateTime.UtcNow,
                            Payload = JsonSerializer.SerializeToElement(new { completion, correlationId = envelope.CorrelationId })
                        };
                        yield break;
                    }
                }
                await Task.Delay(100, ct);
            }
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "DeviceTimeout",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(new { error = "device completion timeout", correlationId = envelope.CorrelationId })
            };
        }
        else
        {
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "DeviceCompletion",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(new { status = "no poll available, send assumed complete", result = sendResult, correlationId = envelope.CorrelationId })
            };
        }
    }
}
