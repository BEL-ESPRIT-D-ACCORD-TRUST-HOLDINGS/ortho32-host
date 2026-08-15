# ortho32-host

**ORTHO-32 Windows Host Runtime**

C#/.NET 8 Worker Service. Named pipe IPC. Long-running process that owns the platform spine on Windows.

## What This Is

`ortho32-host` is the always-running Windows background service that ties every ORTHO component together. It is **not** a web server. It is **not** a script. It owns:

- Named pipe IPC server (`\\.\pipe\ortho-host`) — Swift desktop connects here
- `ORTHORouter` — single canonical route dispatcher
- `ORTHOEventBus` — typed runtime event channel
- `SessionService` — authentication, capability sets, session lifecycle
- `ServiceRegistry` — starts/stops/restarts all platform services
- `InferenceService` — bridges to AI gateway via `\\.\pipe\ortho-ai`
- `BuildService` — real compiler execution via `System.Diagnostics.Process`
- `DeviceService` — calls ORTHOBridge C ABI for hardware access
- `AppRegistry` — installed app manifests
- `CapabilityBroker` — scope enforcement

## Architecture

```
Swift Desktop
    |
    | \\.\pipe\ortho-host (named pipe)
    v
ORTHOHostWorker (BackgroundService)
    |
    +-- ORTHORouter ──────────── dispatch all actions
    |
    +-- ORTHOEventBus ────────── publish/subscribe, push to clients
    |
    +-- SessionService ───────── create / lock / unlock sessions
    +-- ServiceRegistry ──────── start / stop / restart services
    +-- CapabilityBroker ─────── scope enforcement
    +-- AppRegistry ──────────── installed app manifests
    |
    +-- InferenceService ─────── \\.\pipe\ortho-ai --> TypeScript AI Gateway
    +-- BuildService ─────────── System.Diagnostics.Process (real compiler)
    +-- DeviceService ────────── ortho_bridge.h C ABI --> ORTHO-32 hardware/sim
```

## IPC Protocol

Every message uses `MessageEnvelope` / `EventEnvelope`:

```json
// Request
{
  "requestId": "req_01",
  "correlationId": "cor_01",
  "source": "swift-desktop",
  "target": "inference",
  "action": "INFERENCE_SUBMIT",
  "payload": { "model": "coding.default", "messages": [...] },
  "capabilityContext": "session-token",
  "timestamp": "2026-08-14T10:00:00Z"
}

// Response / Event
{
  "eventId": "evt_01",
  "correlationId": "cor_01",
  "source": "inference",
  "type": "token",
  "state": "streaming",
  "payload": { "content": "Hello" },
  "timestamp": "2026-08-14T10:00:01Z"
}
```

## Build and Run

Requires .NET 8 SDK.

```powershell
cd ortho32-host
dotnet build
dotnet run
```

The host starts on `\\.\pipe\ortho-host`. Connect with `PipeClient` or the Swift `HostConnection`.

## Integration Test

```powershell
# Set a real provider key first
$env:ORTHO_AI_KEY = "sk-..."

dotnet test
```

The integration test connects `PipeClient`, sends `SESSION_CREATE`, sends `INFERENCE_SUBMIT` with a real provider, asserts streaming `InferenceEvent` chunks return with matching `correlationId`. Fails on any mock/fixture data.

## Language Ownership

| Layer | Language | Rationale |
|---|---|---|
| Host runtime | C#/.NET 8 | Windows-native, Worker Service, named pipes |
| Swift desktop | Swift | UI, shell, application layer |
| AI gateway | TypeScript | Provider adapter glue only |
| Hardware bridge | C/C++ | Native ABI, PCIe, device control |
| Formal proofs | Lean 4 + HOL Light | Theorem checking |
| RTL/chip | SystemVerilog | Hardware implementation |

## Connection Matrix

See `RUNTIME_CONNECTION_MATRIX.json` for honest per-component status.
See `BUILD_STATUS.md` for RUNNING/STUB/MISSING status per service.

## Repos in This Platform

| Repo | What |
|---|---|
| `ortho32-host` | This repo — Windows runtime spine |
| `ORTHO32` | Processor formal proofs + Swift desktop |
| `ortho32-sdk-java` | JVM SDK |
| `ortho32-api` | REST/SSE API |
| `ortho32-ai-gateway` | Model-agnostic inference layer |
| `ortho32-mcp` | MCP server for VS Code / Claude Code |
| `ortho32-isolation` | Hypervisor + formal isolation proofs |


---

## Sovereign Boundary

This repository operates under the **SnapKitty Method**: public by default, sovereign by construction.

```
CODE        → PUBLIC      (this repository)
PROOF       → PUBLIC      (Lean 4 / formal verification artifacts)
SPEC        → PUBLIC      (interfaces, schemas, invariants)
HISTORY     → PUBLIC      (cryptographic provenance, WORM-sealed)

AUTHORITY   → SOVEREIGN   (Bel Esprit D'Accord Irrevocable Trust)
STATE       → SOVEREIGN   (credentials, private data, operational secrets)
EXECUTION   → AUTHORIZED  (requires sovereign state — not in this repo)
```

> **"Here is the machine. You do not own the state it operates on."**

Reading the source does not grant execution authority. Forking the repo does not grant deployment rights. The code is verifiable. The authority is not transferable.

**[→ Full architecture: SOVEREIGN_METHOD.md](./SOVEREIGN_METHOD.md)**

**[→ License terms: LICENSE](./LICENSE)** · **[→ IP estate: NOTICE](./NOTICE)**

---

*Copyright (C) 2026 Bel Esprit D'Accord Irrevocable Trust (EIN 42-697643) · `Ω = TRUST ∧ CODE`*
