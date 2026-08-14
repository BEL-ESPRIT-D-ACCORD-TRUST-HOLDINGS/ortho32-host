using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Ortho32Host.Interfaces;
using Ortho32Host.IPC;
using Ortho32Host.Models;

namespace Ortho32Host.Services;

public class BuildService : IORTHOService
{
    public string Name => "build";
    private readonly ILogger<BuildService> _logger;

    public BuildService(ILogger<BuildService> logger) { _logger = logger; }

    public Task StartAsync(CancellationToken ct) { _logger.LogInformation("BuildService started"); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken ct) { _logger.LogInformation("BuildService stopped"); return Task.CompletedTask; }

    public async IAsyncEnumerable<EventEnvelope> HandleStreamingAsync(MessageEnvelope envelope, [EnumeratorCancellation] CancellationToken ct)
    {
        string command = "";
        string args = "";
        string cwd = "";
        string compiler = "";

        try
        {
            if (envelope.Payload.ValueKind == JsonValueKind.Object)
            {
                if (envelope.Payload.TryGetProperty("command", out var c)) command = c.GetString() ?? "";
                if (envelope.Payload.TryGetProperty("args", out var a)) args = a.GetString() ?? "";
                if (envelope.Payload.TryGetProperty("cwd", out var w)) cwd = w.GetString() ?? "";
                if (envelope.Payload.TryGetProperty("compiler", out var comp)) compiler = comp.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(command) && !string.IsNullOrWhiteSpace(compiler)) command = compiler;
                if (envelope.Payload.TryGetProperty("cmd", out var cmd2) && string.IsNullOrWhiteSpace(command)) command = cmd2.GetString() ?? "";
            }
            else if (envelope.Payload.ValueKind == JsonValueKind.String)
            {
                command = envelope.Payload.GetString() ?? "";
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to parse build payload"); }

        if (string.IsNullOrWhiteSpace(command))
        {
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "BuildError",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(new { error = "missing command", correlationId = envelope.CorrelationId })
            };
            yield break;
        }

        string fileName;
        string arguments;

        var trimmed = command.Trim();
        if (trimmed.StartsWith("echo ", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("echo", StringComparison.OrdinalIgnoreCase))
        {
            fileName = "cmd.exe";
            arguments = "/c " + trimmed + (string.IsNullOrWhiteSpace(args) ? "" : " " + args);
        }
        else if (!string.IsNullOrWhiteSpace(args))
        {
            fileName = command;
            arguments = args;
        }
        else
        {
            var parts = SplitCommand(trimmed);
            fileName = parts.file;
            arguments = parts.args;
        }

        if (string.IsNullOrWhiteSpace(cwd) || !Directory.Exists(cwd)) cwd = Directory.GetCurrentDirectory();

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _logger.LogInformation("BuildService running: {File} {Args} in {Cwd} corr={Corr}", fileName, arguments, cwd, envelope.CorrelationId);

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        var stdoutFull = new System.Text.StringBuilder();
        var stderrFull = new System.Text.StringBuilder();

        try
        {
            if (!proc.Start())
            {
                yield return new EventEnvelope
                {
                    EventId = Guid.NewGuid().ToString(),
                    CorrelationId = envelope.CorrelationId,
                    EventType = "BuildError",
                    Source = Name,
                    Timestamp = DateTime.UtcNow,
                    Payload = JsonSerializer.SerializeToElement(new { error = "failed to start process" })
                };
                yield break;
            }
        }
        catch (Exception ex)
        {
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "BuildError",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(new { error = ex.Message, file = fileName, args = arguments })
            };
            yield break;
        }

        var stdoutTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardOutput.ReadLineAsync(ct)) != null)
            {
                stdoutFull.AppendLine(line);
            }
        }, ct);

        var stderrTask = Task.Run(async () =>
        {
            string? line;
            while ((line = await proc.StandardError.ReadLineAsync(ct)) != null)
            {
                stderrFull.AppendLine(line);
            }
        }, ct);

        while (!proc.HasExited)
        {
            await Task.Delay(50, ct);
            if (ct.IsCancellationRequested) { try { proc.Kill(true); } catch { } break; }
        }

        try { await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(TimeSpan.FromSeconds(2), ct); } catch { }

        try { await proc.WaitForExitAsync(ct); } catch { }

        string stdoutText = stdoutFull.ToString();
        string stderrText = stderrFull.ToString();

        var lines = stdoutText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var l in lines)
        {
            if (string.IsNullOrEmpty(l) && lines.Length > 1) continue;
            if (string.IsNullOrEmpty(l)) continue;
            var be = new BuildEvent { Stdout = l, Stderr = "", ExitCode = null, Phase = "stdout", CorrelationId = envelope.CorrelationId, Timestamp = DateTime.UtcNow };
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "BuildEvent",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(be)
            };
        }

        if (!string.IsNullOrWhiteSpace(stderrText))
        {
            var beErr = new BuildEvent { Stdout = "", Stderr = stderrText.Trim(), ExitCode = null, Phase = "stderr", CorrelationId = envelope.CorrelationId, Timestamp = DateTime.UtcNow };
            yield return new EventEnvelope
            {
                EventId = Guid.NewGuid().ToString(),
                CorrelationId = envelope.CorrelationId,
                EventType = "BuildEvent",
                Source = Name,
                Timestamp = DateTime.UtcNow,
                Payload = JsonSerializer.SerializeToElement(beErr)
            };
        }

        var final = new BuildEvent
        {
            Stdout = stdoutText.Trim(),
            Stderr = stderrText.Trim(),
            ExitCode = proc.HasExited ? proc.ExitCode : -1,
            Phase = "done",
            CorrelationId = envelope.CorrelationId,
            Timestamp = DateTime.UtcNow
        };
        yield return new EventEnvelope
        {
            EventId = Guid.NewGuid().ToString(),
            CorrelationId = envelope.CorrelationId,
            EventType = "BuildCompleted",
            Source = Name,
            Timestamp = DateTime.UtcNow,
            Payload = JsonSerializer.SerializeToElement(final)
        };
    }

    private static (string file, string args) SplitCommand(string command)
    {
        var idx = command.IndexOf(' ');
        if (idx < 0) return (command, "");
        return (command[..idx], command[(idx + 1)..]);
    }
}
