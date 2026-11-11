# CLAUDE.md

Guidance for AI assistants working in this repository. The project is maintained by a solo developer; keep things simple and boring.

## Goals (in priority order)
1. **Performance / low overhead** – target is a 2 vCPU / 4 GB VPS shared with voidlog.
2. **Maintainability** – one backend language (C#), few moving parts, no message brokers.

Read [docs/PLAN.md](docs/PLAN.md) before larger changes; it records decisions and benchmark data.

## Architecture rules
- **All Elite Insights access goes through `backend/src/Breakbar.Processing/EliteInsights/EliteInsightsAdapter.cs`.** Nothing else references EI types.
- Reference only the EI libraries (`GW2EIEvtcParser`, `GW2EIBuilders`, `GW2EIGW2API`, transitively `GW2EIJSON`). **Never reference `GW2EIParserCommons`** or EI's `Properties.Settings`/`ProgramHelper`: they use System.Configuration user settings and crash on headless Linux.
- EI runs **in-process**, one log per worker thread, always `multiThreadAcceleration: false` (benchmark: multi-threading inside a log gains nothing on the VPS; 2 logs in parallel give ~1.7x).
- HTML uses external EI assets (`HtmlExternalScripts`) served from a configurable base URL; never let EI write `file://` links.
- Never edit files inside `external/` – EI is a pinned submodule. Workarounds belong in the adapter.
- Parsing is scheduled only through `FairParseQueue` + `ParseWorkerPool` (`backend/src/Breakbar.Processing/Queue/`): round-robin per user, memory-aware admission, no overtaking of the head job. Do not start parses elsewhere.
- Planned: SQLite, SSE, Caddy on the host serves reports and frontend from disk. No Redis, no Postgres, no extra containers unless PLAN.md is updated first.

## Conventions
- .NET 10, nullable enabled, warnings are errors (`backend/Directory.Build.props`).
- Tests: xUnit v3 on Microsoft.Testing.Platform: `dotnet test --solution Breakbar.slnx -c Release` from `backend/`.
- Real arcdps logs are never committed (size, player names). Tests use `BREAKBAR_TEST_LOG_DIR` or `benchmark/logs/` and skip when none exist.
- Commit each finished part of a task as its own focused commit.
- Docs and plan are written in German; code, comments and commit messages in English.
