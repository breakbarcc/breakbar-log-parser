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
- API (`backend/src/Breakbar.Api/`, endpoints documented in PLAN.md 4a): raw-body uploads streamed to disk while hashing, SHA-256 dedupe, SQLite via Dapper with **all SQL in `Persistence/JobStore.cs`**, live status via SSE (`JobEventHub`), the API itself serves reports and EI assets; Caddy on the host only does TLS, compression and proxying.
- **Security (PLAN.md 4b):** report HTML and EI assets are served only on the reports origin (`Reports:BaseUrl`, production `https://reports.breakbar.cc`); never serve report content on the API origin, never add CORS to the API, never give the identity cookie a `Domain` attribute. Uploads are fully decompressed once with a hard limit (zip bombs) and are subject to per-user/total/disk limits.
- No Redis, no Postgres, no extra containers unless PLAN.md is updated first.
- Do not name code folders `data`/`Data`: runtime data lives in `data/` and is git-ignored (Windows paths are case-insensitive).

## Frontend (`frontend/`, PLAN.md 5a)
- React 19 + TypeScript + Vite + Tailwind 4 + React Router; design tokens live in `src/styles.css` (`@theme`). Use the tokens, not new hex values.
- Everything self-hosted (fonts via `@fontsource`, no CDNs, no Google Fonts): the API sends a strict CSP (`default-src 'self'`).
- All API calls in `src/lib/api.ts`; upload/job state in `src/lib/uploads.tsx`; pure logic in `src/lib/rows.ts` with vitest tests.
- UI text is German. `npm run build` writes to `backend/src/Breakbar.Api/wwwroot` (git-ignored).

## Conventions
- .NET 10, nullable enabled, warnings are errors (`backend/Directory.Build.props`).
- Tests: xUnit v3 on Microsoft.Testing.Platform: `dotnet test --solution Breakbar.slnx -c Release` from `backend/`.
- Real arcdps logs are never committed (size, player names). Tests use `BREAKBAR_TEST_LOG_DIR` or `benchmark/logs/` and skip when none exist.
- Commit each finished part of a task as its own focused commit.
- Docs and plan are written in German; code, comments and commit messages in English.
