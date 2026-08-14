| Component | Language | Status | IPC | IntegrationTest |
|---|---|---|---|---|
| AI Gateway Pipe Server (`src/ipc/pipe-server.ts`) | TypeScript (Node.js) | RUNNING | Named pipe `ortho-ai` (`\\.\pipe\ortho-ai` on Windows, `/tmp/ortho-ai.sock` on POSIX) - MessageEnvelope JSON lines, streams InferenceEvent chunks, handles disconnect without crash | `tests/integration/test_pipe_roundtrip.ts` - in-process server + client roundtrip |
| AI Gateway Inference Handler (`src/ipc/inference-handler.ts`) | TypeScript | RUNNING | Pipe in -> HTTPS to OpenAI/Anthropic -> Pipe out (SSE streaming) | `test_pipe_roundtrip.ts` asserts token events + `correlationId` match + final `type==completed`; FAILS if key missing (no fake streaming) |
| AI Gateway Models Handler (`src/ipc/models-handler.ts`) | TypeScript | RUNNING | Pipe in -> live `GET /v1/models` -> Pipe out | Manual: requires `OPENAI_API_KEY`/`ANTHROPIC_API_KEY`, returns live catalog or `failed`; no fake data |
| AI Gateway Entry Point (`src/ipc/start.ts`) | TypeScript | RUNNING | Loads providers from env, logs ready, starts pipe server | Covered by pipe roundtrip |
| Bridge Simulator ABI (`sim/ortho_sim.h`) | C | RUNNING | C ABI `ortho_sim_open/submit/close` | `sim/test_sim.c` |
| Bridge Simulator Core (`sim/ortho_sim.c`) | C | RUNNING | TENSOR opcode real 4x4 matmul (cblas_sgemm or manual), `completion.cycles=768`, `traceRoot`/`resultHash`=SHA-256 hex of actual result bytes | `sim/test_sim.c` asserts `cycles==768`, 64-char hex, recomputes hash |
| Bridge Simulator Build (`sim/Makefile`) | Make/C | RUNNING | Compiles to `ortho_sim.so` / `.dll` / `.dylib` (`USE_CBLAS=1` optional) | `make test` runs `test_sim` |
| Bridge Simulator Test (`sim/test_sim.c`) | C | RUNNING | In-process ABI call with identity matrices | `make test` - PASSES |
| ORTHOHost (C#/.NET Router/EventBus/Session) | C#/.NET | STUB | Expected client to `ortho-ai` pipe - Gateway ready to accept | MISSING - marked mocked in matrix |
| ORTHORouter (canonical routes) | C#/.NET | STUB | Envelope `source/target/action` fields prepared | MISSING |
| Swift Desktop | Swift | MISSING | Expected client to `ortho-ai` pipe | MISSING |
