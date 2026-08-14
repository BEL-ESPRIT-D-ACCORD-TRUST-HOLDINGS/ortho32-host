| Layer | Language | Rationale |
|---|---|---|
| ORTHOHost - IPC Server, Router, EventBus, Session, Service Registry | C# / .NET | Native Windows named pipe support, strong typing for router/intents, integrates with existing .NET desktop host lifecycle and service registry |
| ORTHORouter - Canonical Routes, Intents, Dispatch | C# / .NET | Shares host process for atomic dispatch tables, benefits from .NET dependency injection and compile-time route verification |
| ORTHOEventBus - Runtime Events, Subscriptions, Streaming, Correlation IDs | C# / .NET | Requires cross-process publish/subscribe within host; .NET Channels and async streams provide backpressure and correlation tracking |
| ORTHOAI - AI Gateway (Inference & Models) | TypeScript (Node.js) | Best ecosystem for AI provider SDKs (OpenAI, Anthropic) with native fetch/SSE streaming; Node `net` module implements `\\.\pipe\ortho-ai` IPC without native addons; rapid iteration on prompt handling |
| ORTHOBridge - Hardware Simulator / Protocol (TENSOR, DMA) | C / C++ | Cycle-accurate performance (architectural constant `cycles=768`), deterministic memory layout, direct SHA-256 over output bytes, compiles to `ortho_sim.dll/.so` for host interop via P/Invoke; optional `cblas_sgemm` for optimized matmul |
| Swift Desktop - UI Client | Swift | Native macOS desktop UI, connects to ORTHOHost via named pipe as secondary client; language ownership keeps UI rendering separate from runtime spine |
| Integration Tests - Pipe Roundtrip | TypeScript | Runs in-process gateway server, exercises real IPC framing and provider streaming with `correlationId` assertions |
| Integration Tests - Bridge Sim | C | Direct ABI verification without host, asserts architectural invariants (768 cycles, hex hash) |
