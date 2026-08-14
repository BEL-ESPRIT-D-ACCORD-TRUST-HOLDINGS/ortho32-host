using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Ortho32Host.IPC;
using Ortho32Host.Router;

namespace Ortho32Host.Integration;

public static class IntegrationTest
{
    public static async Task RunAsync(IHost host)
    {
        var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IntegrationTest");
        logger.LogInformation("=== ORTHO32 IntegrationTest starting (in-process) ===");

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await host.StartAsync(cts.Token);
            logger.LogInformation("Host started, waiting for pipe ortho-host to be ready");
            await Task.Delay(1500, cts.Token);

            await TestInferenceStreaming(logger, cts.Token);
            await TestBuildRealProcess(logger, cts.Token);
            await TestDeviceBridgeUnavailable(logger, cts.Token);

            logger.LogInformation("=== ALL INTEGRATION TESTS PASSED ===");
            Console.WriteLine("=== ALL INTEGRATION TESTS PASSED ===");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "IntegrationTest FAILED");
            Console.WriteLine($"INTEGRATION TEST FAILED: {ex}");
            Environment.ExitCode = 1;
            throw;
        }
        finally
        {
            try { await host.StopAsync(CancellationToken.None); } catch { }
        }
    }

    private static async Task TestInferenceStreaming(ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Test 1: INFERENCE_SUBMIT -> ortho-ai pipe streaming with correlationId");
        var correlationId = Guid.NewGuid().ToString();
        await using var client = new PipeClient("ortho-host");
        await client.ConnectAsync(5000, ct);
        logger.LogInformation("PipeClient connected for inference test corr={Corr}", correlationId);

        var payload = new { prompt = "hello world", model = "test-model", stream = true, maxTokens = 10 };
        var envelope = MessageEnvelope.Create(RouteActions.INFERENCE_SUBMIT, payload, correlationId, "integration-test", "inference");
        await client.SendAsync(envelope, ct);

        var events = new List<EventEnvelope>();
        var deadline = DateTime.UtcNow.AddSeconds(8);
        while (DateTime.UtcNow < deadline)
        {
            EventEnvelope? ev = null;
            try
            {
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(readCts.Token, ct);
                ev = await client.ReadEventAsync(linked.Token);
            }
            catch (OperationCanceledException) { break; }
            if (ev == null) break;
            events.Add(ev);
            logger.LogInformation("Inference event: {Type} corr={Corr} payload={Payload}", ev.EventType, ev.CorrelationId, ev.Payload.GetRawText());
            if (ev.EventType == "StreamCompleted") break;
            if (events.Count > 20) break;
        }

        if (events.Count == 0) throw new Exception("INFERENCE_SUBMIT: no events returned");

        foreach (var e in events)
        {
            if (e.CorrelationId != correlationId) throw new Exception($"INFERENCE_SUBMIT: correlationId mismatch expected {correlationId} got {e.CorrelationId} type {e.EventType}");
        }

        var hasExpected = events.Any(e => e.EventType == "ServiceUnavailableEvent" || e.EventType == "InferenceChunk" || e.EventType == "InferenceDone");
        if (!hasExpected) throw new Exception($"INFERENCE_SUBMIT: expected ServiceUnavailableEvent or InferenceChunk but got {string.Join(",", events.Select(e => e.EventType))}");

        var unavailable = events.FirstOrDefault(e => e.EventType == "ServiceUnavailableEvent");
        if (unavailable != null)
        {
            var raw = unavailable.Payload.GetRawText();
            if (!raw.Contains("ortho-ai")) throw new Exception("ServiceUnavailableEvent should mention ortho-ai pipe");
            logger.LogInformation("INFERENCE_SUBMIT correctly returned ServiceUnavailableEvent when ortho-ai pipe missing - NOT mock data");
        }

        Console.WriteLine($"[PASS] INFERENCE_SUBMIT streaming correlationId={correlationId} events={events.Count} types={string.Join(",", events.Select(e=>e.EventType))}");
        logger.LogInformation("[PASS] INFERENCE_SUBMIT test passed");
    }

    private static async Task TestBuildRealProcess(ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Test 2: BUILD_RUN with echo hello -> real Process stdout BuildEvent");
        var correlationId = Guid.NewGuid().ToString();
        await using var client = new PipeClient("ortho-host");
        await client.ConnectAsync(5000, ct);

        var payload = new { command = "echo hello", args = "", cwd = "" };
        var envelope = MessageEnvelope.Create(RouteActions.BUILD_RUN, payload, correlationId, "integration-test", "build");
        await client.SendAsync(envelope, ct);

        var events = new List<EventEnvelope>();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            EventEnvelope? ev = null;
            try
            {
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(readCts.Token, ct);
                ev = await client.ReadEventAsync(linked.Token);
            }
            catch (OperationCanceledException) { break; }
            if (ev == null) break;
            events.Add(ev);
            logger.LogInformation("Build event: {Type} corr={Corr} payload={Payload}", ev.EventType, ev.CorrelationId, ev.Payload.GetRawText());
            if (ev.EventType == "StreamCompleted") break;
            if (ev.EventType == "BuildCompleted") { continue; }
            if (events.Count > 30) break;
        }

        if (events.Count == 0) throw new Exception("BUILD_RUN: no events returned");

        foreach (var e in events)
        {
            if (e.CorrelationId != correlationId) throw new Exception($"BUILD_RUN: correlationId mismatch expected {correlationId} got {e.CorrelationId}");
        }

        var stdoutFound = false;
        foreach (var e in events)
        {
            var raw = e.Payload.GetRawText();
            if (raw.Contains("hello", StringComparison.OrdinalIgnoreCase)) stdoutFound = true;
            try
            {
                if (e.Payload.ValueKind == JsonValueKind.Object)
                {
                    if (e.Payload.TryGetProperty("Stdout", out var s) && s.GetString()?.Contains("hello", StringComparison.OrdinalIgnoreCase) == true) stdoutFound = true;
                    if (e.Payload.TryGetProperty("stdout", out var s2) && s2.GetString()?.Contains("hello", StringComparison.OrdinalIgnoreCase) == true) stdoutFound = true;
                }
            }
            catch { }
        }

        if (!stdoutFound) throw new Exception($"BUILD_RUN: expected stdout containing 'hello' from real process, got events: {string.Join(" | ", events.Select(e => e.Payload.GetRawText()))}");

        var completed = events.Any(e => e.EventType == "BuildCompleted");
        if (!completed) throw new Exception("BUILD_RUN: missing BuildCompleted event");

        Console.WriteLine($"[PASS] BUILD_RUN echo hello stdout verified corr={correlationId} events={events.Count}");
        logger.LogInformation("[PASS] BUILD_RUN test passed");
    }

    private static async Task TestDeviceBridgeUnavailable(ILogger logger, CancellationToken ct)
    {
        logger.LogInformation("Test 3: DEVICE_COMMAND -> ORTHOBridge C ABI or ServiceUnavailableEvent");
        var correlationId = Guid.NewGuid().ToString();
        await using var client = new PipeClient("ortho-host");
        await client.ConnectAsync(5000, ct);
        var payload = new { command = "status", device = "sim0" };
        var envelope = MessageEnvelope.Create(RouteActions.DEVICE_COMMAND, payload, correlationId, "integration-test", "device");
        await client.SendAsync(envelope, ct);

        var events = new List<EventEnvelope>();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            EventEnvelope? ev = null;
            try
            {
                using var readCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(readCts.Token, ct);
                ev = await client.ReadEventAsync(linked.Token);
            }
            catch (OperationCanceledException) { break; }
            if (ev == null) break;
            events.Add(ev);
            if (ev.EventType == "StreamCompleted") break;
            if (events.Count > 10) break;
        }

        if (events.Count == 0) throw new Exception("DEVICE_COMMAND: no events");
        foreach (var e in events) if (e.CorrelationId != correlationId) throw new Exception("DEVICE_COMMAND correlationId mismatch");

        var hasRelevant = events.Any(e => e.EventType == "ServiceUnavailableEvent" || e.EventType == "DeviceAck" || e.EventType == "DeviceCompletion");
        if (!hasRelevant) throw new Exception($"DEVICE_COMMAND: expected ServiceUnavailableEvent or DeviceAck, got {string.Join(",", events.Select(e=>e.EventType))}");

        Console.WriteLine($"[PASS] DEVICE_COMMAND bridge check corr={correlationId} types={string.Join(",", events.Select(e=>e.EventType))}");
        logger.LogInformation("[PASS] DEVICE_COMMAND test passed");
    }
}
