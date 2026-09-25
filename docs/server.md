---
title: Server
---

# 🖥️ Centurion.Server — REST API for the Whole Pipeline 🚀

**Centurion.Server** is a separate ASP.NET Core service that exposes every packaged command over HTTP — no console, no JSON config files, just `POST` and go. It runs the exact same command kernel as the CLI (same DI container, same `Settings` binding, same operators), so a request behaves identically to a CLI invocation. 🔌

**Why it exists:** the CLI's old `--config <FILE>` flag and the embedded `server` command (HttpListener) are gone. JSON-driven invocation now lives *only* in the Server project — one contract, one place to maintain.

## 🏁 Run it

```bash
dotnet run --project Centurion.Server            # or run the published exe
# default: http://localhost:5000 — override with ASPNETCORE_URLS
```

## 🔗 Endpoints

| Method & Path | What it does |
|---|---|
| `GET /` | Service blurb (name + endpoints) |
| `GET /health` | Liveness check → `{ "status": "ok", "commands": 7 }` |
| `GET /commands` | List of executable commands |
| `GET /version` | Name + **build date** (no version numbers, as promised 😉) |
| `POST /commands/{name}` | Execute a command → `{ command, exitCode, durationMs, startedAt, finishedAt }` |

## 📦 Request body

Either a full `CommandRequest` (`{ "command", "parameters" }`) or a bare parameters object — both are accepted:

```json
// POST /commands/spawn
{
  "parameters": {
    "language": "zh",
    "num-speakers": 2,
    "splitter": { "target-length": 45 }   // nested objects → parent.child
  }
}
```

Key rules (same as the old config contract):

- Keys match property names in `kebab-case` / `snake_case` / `camelCase` — all equivalent
- Nested objects expand to `parent.child` (leaf key is also tried)
- Unknown keys are warned and ignored (forward-compatible)
- Values auto-convert: `int` / `double` / `bool` / `string` / enums / file paths

## 🧪 Try it

```bash
curl -X POST http://localhost:5000/commands/build \
  -H "Content-Type: application/json" \
  -d '{"centurion-file": "demo.centurion.json"}'
# → {"command":"build","exitCode":0,"durationMs":296,"startedAt":"...","finishedAt":"..."}
```

**Adding a command:** register it in `Centurion.Server/Commands/ServerCommandRegistry.cs` and it appears on the API instantly — no other wiring needed. ✨

## 🔀 Relationship to the CLI

- CLI remains the primary human interface; Server is the automation face (CI, web UIs, future JSON-RPC).
- Errors follow the command kernel: command-level failures return `exitCode != 0` with HTTP 200; contract/routing failures return proper HTTP 4xx/5xx.
- Command output is routed into Server's `ILogger` (see `Console/LoggerConsoleOutput.cs`), so long-running pipelines stay observable in server logs. 📋
