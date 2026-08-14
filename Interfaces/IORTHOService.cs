using Ortho32Host.IPC;
using System.Runtime.CompilerServices;

namespace Ortho32Host.Interfaces;

public interface IORTHOService
{
    string Name { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    IAsyncEnumerable<EventEnvelope> HandleStreamingAsync(MessageEnvelope envelope, CancellationToken ct);
}
