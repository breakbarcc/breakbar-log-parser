# breakbar-log-parser

Self-hosted service for parsing Guild Wars 2 arcdps logs (`.evtc`, `.zevtc`) with [Elite Insights](https://github.com/baaron4/GW2-Elite-Insights-Parser) (EI) into HTML reports and JSON. Planned public instance: `logs.breakbar.cc`; it will also serve as the parsing backend for [voidlog](https://github.com/breakbarcc/voidlog).

Status: **early skeleton** – EI runs in-process and produces reports; upload, fair queue, storage and frontend are not built yet. See [docs/PLAN.md](docs/PLAN.md) for architecture, decisions and benchmark results.

## Repository layout

| Path | Content |
|---|---|
| `backend/` | .NET 10 solution `Breakbar.slnx` |
| `backend/src/Breakbar.Processing/` | `EliteInsightsAdapter` – the only code that talks to EI |
| `backend/src/Breakbar.Api/` | ASP.NET Core API (currently only `/api/health`) |
| `backend/tools/Breakbar.ParseSpike/` | console tool: parse logs in-process, print time and memory |
| `backend/tests/` | xUnit v3 tests |
| `external/GW2-Elite-Insights-Parser/` | EI as git submodule, pinned to a release tag |
| `benchmark/` | EI CLI benchmark scripts and results |
| `docs/PLAN.md` | project plan |

## Getting started

Requirements: .NET 10 SDK, git.

```bash
git clone --recurse-submodules <repo-url>
cd breakbar-log-parser/backend
dotnet build -c Release
dotnet test --solution Breakbar.slnx -c Release
```

Tests that need a real log look in `benchmark/logs/` (not committed) or in the folder set via `BREAKBAR_TEST_LOG_DIR`, and are skipped otherwise.

Parse logs in-process:

```bash
dotnet run --project tools/Breakbar.ParseSpike -c Release -- ../benchmark/logs --rounds 2
```

Run the API (http://localhost:5080/api/health):

```bash
dotnet run --project src/Breakbar.Api
```

## Updating Elite Insights

```bash
cd external/GW2-Elite-Insights-Parser
git fetch --depth 1 origin tag vX.Y.Z.W
git checkout vX.Y.Z.W
cd ../..
git add external/GW2-Elite-Insights-Parser
```

Then build, run the tests, and adjust `EliteInsightsAdapter.cs` if the EI API changed.

## License

Elite Insights is MIT licensed (see the submodule's LICENSE).
