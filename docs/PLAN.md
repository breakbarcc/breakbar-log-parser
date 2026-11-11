# breakbar-log-parser – Projektplan (Entwurf)

> Stand: 2026-11-11 · Status: Planung, noch keine Implementierung
> Ziele in Priorität: **1. Performance / wenig Overhead auf schwachem VPS** · **2. leichte Wartbarkeit (Solo-Dev + KI)**

---

## 1. Ergebnisse der Recherche: Elite Insights (EI)

| Punkt | Befund |
|---|---|
| Sprache | C# / **.NET 10** (seit v3.32.0.0, 2026-11-08) |
| Lizenz | **MIT** → Einbinden als Bibliothek erlaubt (Attribution beibehalten) |
| Plattform | Linux x64/arm64 offiziell unterstützt (zip, .deb, .rpm) |
| CLI | `dotnet GuildWars2EliteInsights-CLI.dll -c <config.conf> <logs...>` – gibt pro Log eine Zeile `Processed - {...}` mit `generatedFiles`, `elapsed`, `reason` aus |
| Bibliotheken im Repo | `GW2EIEvtcParser` (EVTC parsen + Kampfanalyse), `GW2EIBuilders` (HTML/JSON/CSV erzeugen), `GW2EIGW2API` (GW2-API-Cache), `GW2EIJSON` (JSON-Modelle), `GW2EIParserCommons` |
| NuGet | **Keine** NuGet-Pakete → Einbindung als Git-Submodule + `ProjectReference` |

**Relevante Settings** (Namen exakt aus dem README):

- Output: `SaveOutHTML`, `SaveOutJSON`, `CompressRaw` (JSON gzip), `IndentJSON` (aus!), `SaveOutCSV`
- HTML-Größe: `HtmlExternalScripts`, `HtmlExternalScriptsCdn` (JS/CSS nur einmal ausliefern), `HtmlCompressJson` (~60 % kleiner)
- Parallelität: `SingleThreaded` (ein Log nutzt nur einen Thread), `ParseMultipleLogs`
- Arbeitsumfang: `ParsePhases`, `ParseCombatReplay`, `ComputeBuff/Damage/Cast/Mechanics/DamageModifiers`, `SkipFailedTries`, `DetailledWvW` (**aus** – Laufzeit/Größe explodiert)
- Schutz: `MemoryLimit` (MB, Exit-Code 2 bei Überschreitung)

**JSON-Export:** Ja, `SaveOutJSON` (+ `CompressRaw` → `.json.gz`). Gut als API-Output und für spätere Features (Statistiken, Suche), ohne die HTML zu parsen.

---

## 1b. Zielsystem: VPS und Nachbar „voidlog“

| Ressource | Wert | Konsequenz |
|---|---|---|
| CPU | **2 vCPU** (AMD64) | bis zu **2 Parses gleichzeitig** (je 1 Thread); voidlog hat aktuell wenig Last |
| RAM | **4 GiB** | Parser bekommt hartes Limit ~**2,8 GiB**, Jobs werden nach RAM-Bedarf zugelassen |
| Disk | 128 GiB **HDD**, ~100 GiB frei, sehr niedrige IOPS | wenig, dafür sequenzielle Schreibvorgänge; Retention per Speicher-Quota |
| OS | Debian 13 | .NET 10 läuft nativ bzw. im Container |

Das CPU-Diagramm des Hosters zeigt „OP/s“, keine Auslastung in %. Die echte Grundlast vor Projektstart mit `docker stats` / `htop` messen.

**Bereits auf dem VPS: [voidlog](https://github.com/breakbarcc/voidlog)** (Docker Compose: Next.js-Web auf `127.0.0.1:4000`, BullMQ-Worker, Postgres 16, Redis 7; **keine** Ressourcen-Limits gesetzt; Reverse-Proxy Caddy laut Compose-Kommentar „geplant“).

Geschätztes RAM-Budget (per Messung bestätigen):

| Komponente | RAM ca. |
|---|---|
| Debian + Docker | 300 MB |
| voidlog (Next.js, Worker, Postgres, Redis), mit Limit | ~0,8 GB |
| Caddy (gemeinsam) + Monitoring-Agent | 70 MB |
| **breakbar-log-parser API im Leerlauf** | 80–120 MB |
| **verfügbar für Parses** | **~2,6 GB** |

Daraus folgt:
1. **Zwei Worker** (`N = 2`) mit **RAM-gesteuerter Zulassung** (Details in Abschnitt 3), durch den Benchmark bestätigt (1c):
   - jeder Job läuft mit `SingleThreaded=true`; zwei Jobs parallel ergeben ~1,8× Durchsatz.
   - großer Log (z. B. WvW) → läuft **exklusiv**, der zweite Worker wartet.
   - Container: `cpus: 2.0`, aber niedrigere CPU-Priorität (`cpu_shares: 512`) → voidlog gewinnt bei Konkurrenz, nutzt aber nichts, wenn es idle ist.
2. **Hartes Speicherlimit** für den Container (`mem_limit: 2.8g`), `GCHeapHardLimit` ≈ 2,4 GB und EI-`MemoryLimit` pro Parse. Passt ein Log auch allein nicht ins Budget, wird er sauber als „zu groß“ abgelehnt, statt den Server zu blockieren.
3. **2–4 GB Swapfile** als Sicherheitsnetz gegen OOM-Kills (auf HDD langsam, nur Notfall).
4. **Auch für voidlog `mem_limit` setzen**, sonst kann voidlog den Parser verdrängen und umgekehrt.
5. **Caddy läuft bereits direkt auf dem Host** (belegt Port 80/443, kein Container) und wird mitgenutzt für `voidlog.breakbar.cc` → `127.0.0.1:4000` und **`logs.breakbar.cc`** → Frontend, Reports und Parser-API. Das neue Projekt nutzt **nicht** Port 4000.
6. **Kein eigenes Postgres/Redis** – SQLite reicht und spart ~300 MB.
7. HDD: Uploads direkt als Stream auf Disk, `.zevtc` nicht vorab entpacken (EI liest es selbst), pro Report nur **eine** vorkomprimierte Variante (gzip), SQLite im WAL-Modus.

**Synergie:** voidlog lässt Logs aktuell über dps.report parsen. Später kann voidlogs Worker stattdessen die interne API dieses Dienstes nutzen und bekommt EI-JSON direkt, ohne externe Abhängigkeit und Rate-Limits. Die API daher von Anfang an so bauen, dass ein „Service-Client“ (API-Key, eigener Fair-Queue-User) möglich ist.

---

## 1c. Benchmark-Ergebnisse (lokal, 2026-11-11)

Rechner: Ryzen 9 5900X, 32 GB, Windows 11 · EI v3.32.0.0, .NET 10.0.15 · Combat Replay an · 13 PvE-Logs (Raid, Golem, Wartime-Strike), 0,1–3,9 MB `.zevtc` (0,7–22,6 MB entpackt) · jeder Durchlauf zweimal, Werte stabil (±2 %). Rohdaten: `benchmark/results/ATOMKRAFTWERK-20261111-142053/`.

| Batch | Modus | Gesamtzeit 13 Logs | Logs/Stunde | RAM-Spitze gesamt |
|---|---|---|---|---|
| A | 1 Log, `SingleThreaded=false` | 48,2 s | ~970 | 0,96 GB |
| B | 1 Log, `SingleThreaded=true` | 51,4 s | ~910 | 0,96 GB |
| **C** | **2 Logs parallel, `SingleThreaded=true`** | **27,2 s** | **~1720** | **1,24 GB** |

Erkenntnisse:
1. **EI nutzt innerhalb eines Logs kaum mehrere Kerne:** Multi-Thread (A) ist nur ~7 % schneller als Single-Thread (B). Zwei Logs parallel (C) schaffen dagegen **1,8× so viele Logs** wie A. → **Immer `SingleThreaded=true`**, Parallelität nur über mehrere Logs. Das dynamische Umschalten aus Abschnitt 3 entfällt (einfacher, praktisch kein Nachteil für Einzel-User).
2. **RAM pro Log** wächst etwa linear mit der entpackten EVTC-Größe: 0,7 MB → 130 MB, 6,7 MB → 300 MB, 15 MB → 450–580 MB, 22,6 MB → 960 MB. Konservative Schätzformel: **`RAM ≈ 120 MB + 42 × EVTC-MB`**. Im späteren Dienst wird es pro Job etwas weniger sein, weil die .NET-Runtime und die Skill-Listen (~10 MB JSON) nur einmal geladen werden.
3. **Parse-Zeit** lokal 1,2–7,7 s pro Log (Mittel ~3,9 s). Auf den vCPUs des VPS ist mit dem 1,5- bis 2,5-Fachen zu rechnen → grob **600–1000 Logs/Stunde** bei 2 parallelen Jobs. *Muss auf dem VPS bestätigt werden.*
4. **Speicher pro Report:** HTML 0,07–2,9 MB, JSON (gzip) 0,05–2,8 MB, zusammen im Mittel ~2,5 MB. HTML lässt sich mit gzip nur noch um ~25 % verkleinern (Daten sind durch `HtmlCompressJson` schon komprimiert). JS/CSS von EI (~820 KB) liegen dank `HtmlExternalScripts` nur einmal pro EI-Version auf der Platte. → 60 GB Quota ≈ 24 000 Reports.
5. **Wichtig für Produktion:** Ohne `HtmlExternalScriptsCdn` schreibt EI `file://`-Pfade zu den Skripten in die HTML. In Produktion muss `HtmlExternalScriptsCdn=https://logs.breakbar.cc/ei-assets/` gesetzt sein. Die EI-HTML lädt zusätzlich Bootstrap u. a. von `cdnjs.cloudflare.com` (bei Bedarf später selbst hosten).

### VPS-Ergebnisse (2026-11-11, gleiche 13 Logs)

VPS: 2 vCPU AMD EPYC Genoa, 3,9 GB RAM, Debian 13 · EI mit `nice 10`, voidlog lief nebenher (Leerlauf, zusammen ~420 MB) · vor dem Lauf ~1,0 GB RAM belegt. Rohdaten: `benchmark/results-vps/vps-20261111-171016/`.

| Batch | Modus | Gesamtzeit 13 Logs | Logs/Stunde | RAM-Anstieg (System) |
|---|---|---|---|---|
| A | 1 Log, `SingleThreaded=false` | 75,7 s | ~620 | 0,80 GB |
| B | 1 Log, `SingleThreaded=true` | 75,7 s | ~620 | 0,80 GB |
| **C** | **2 Logs parallel** | **43,9 s** | **~1070** | **1,15 GB** |

- Bestätigt: **2 parallele Jobs mit `SingleThreaded=true`** (1,72× Durchsatz). Multi-Threading innerhalb eines Logs bringt auf 2 vCPUs **gar nichts** (A = B).
- Der VPS ist ~1,6× langsamer als der Desktop: 1,5–12,3 s pro Log, im Mittel 5,8 s; mit 2 Workern effektiv ~3,4 s pro Log.
- RAM pro Log unter Linux etwas höher als unter Windows (153–976 MB). Angepasste Schätzformel: **`RAM ≈ 150 MB + 42 × EVTC-MB`** (deckt alle Messwerte ab).
- Höchste RAM-Belegung im System während C: ~2,2 GB von 3,9 GB → für PvE viel Luft.
- Einordnung: Ein Raid-Abend mit 10 Leuten × 15 Logs = 150 Logs wäre bei gleichzeitigem Upload nach ~8–9 Minuten komplett verarbeitet. Die Fair Queue sorgt dafür, dass jeder User seine ersten Logs nach Sekunden statt erst am Ende bekommt.
- Linux-Stolpersteine beim CLI-Start (fehlendes `libicu`, fehlendes `~/.local/share`) → siehe Abschnitt 6b.

**Noch offen:** große WvW-Logs (nicht verfügbar). Nach der Formel bräuchte ein WvW-Log mit 60 MB entpacktem EVTC ~2,6 GB RAM und würde das Budget sprengen. Bis Messwerte vorliegen: Jobs mit Schätzung > 1,2 GB laufen **exklusiv**, Jobs mit Schätzung > 2,4 GB werden abgelehnt.

---

## 2. Kernentscheidung: EI in-process als Bibliothek statt CLI-Aufruf pro Log

| | **A: In-process (empfohlen)** | B: CLI pro Job spawnen |
|---|---|---|
| Startkosten pro Log | keine (JIT warm, Caches im RAM) | .NET-Start + JIT jedes Mal (~0,3–1 s, viel RAM-Churn) |
| GW2-API-Cache | einmal geladen, geteilt | pro Prozess neu |
| Steuerung | Settings pro Job dynamisch (z. B. `SingleThreaded` je nach Last), Cancel, Fortschritt | nur Config-Datei + stdout parsen |
| Risiko | interne EI-API kann sich bei Updates ändern; Absturz/OOM trifft den Dienst | stabil-isoliert |

**Konsequenz → Backend in C#/.NET 10.** Jede andere Sprache (Go, Rust, Node) müsste EI als Fremdprozess aufrufen und verliert genau den Performance-Vorteil. Gleiche Sprache wie EI heißt außerdem: ein Toolchain, ein Debugger, KI kann EI-Quellcode direkt mitlesen.

**Rust als Backend geprüft und verworfen (2026-11-11):** EI ist nur als .NET-Code verfügbar. Ein Rust-Backend müsste entweder pro Log einen EI-Prozess starten oder einen dauerhaft laufenden C#-Worker ansprechen (zwei Sprachen) oder die .NET-Runtime in Rust einbetten (hostfxr, fehleranfällig). Laut Benchmark hat jeder Log einen festen Anteil von ~1,2 s (Prozessstart, JIT, Laden der Skill-Listen), bei einem mittleren Log von ~3,9 s also bis zu ~30 %. In-process fällt der größte Teil davon nur einmal an. Über 90 % der CPU-Zeit verbraucht EI selbst, Rust würde also nur beim schlanken Web-Teil sparen (~50 MB RAM).

**Risiko-Abfederung:**
1. Sämtlicher EI-Zugriff hinter **einer** Klasse `EliteInsightsAdapter` (≈ 100–200 Zeilen). EI-Update = Submodule auf neuen Release-Tag + nur diese Datei anpassen.
2. Falls In-process-Abstürze/OOM real auftreten: Fallback-Stufe **„langlebiger Worker-Prozess“** (gleicher Code, separater Prozess, spricht per stdin/stdout oder Unix-Socket) – *nicht* ein Prozess pro Log. Erst bauen, wenn nötig.
3. Pro Job harte Limits: Upload-Größe, Timeout via `CancellationToken`, globaler RAM-Budget-Check vor Job-Start.

---

## 3. Fair Queue pro User – das Pattern

**Ja, das ist ein bekanntes Pattern:** *Fair Queuing* über Mandanten/User („noisy neighbor“-Problem), konkret **Round-Robin über per-User-Queues** bzw. die gewichtete Variante **Deficit Round Robin / Weighted Fair Queuing**. Die gewünschte Eigenschaft „ein einzelner User bekommt alle Worker“ heißt **work-conserving**: ungenutzte Anteile werden an die aktiven User umverteilt. So arbeiten u. a. Temporal (Fairness Keys, weighted round-robin), Amazon SQS Fair Queues und BullMQ-Groups.

### Algorithmus (in-process, ohne Redis)

```
State:
  queues:  Dictionary<UserId, Queue<Job>>
  ring:    LinkedList<UserId>          // nur User mit wartenden Jobs
  signal:  SemaphoreSlim(0)            // zählt wartende Jobs

Enqueue(user, job):
  lock { queues[user].Enqueue(job); if user nicht im ring → ring.AddLast(user) }
  signal.Release()

Worker-Loop (N Worker):
  await signal.WaitAsync()
  lock {
    user = ring.First; ring.RemoveFirst()
    job  = queues[user].Dequeue()
    if queues[user].Count > 0 → ring.AddLast(user)   // hinten anstellen
  }
  run(job)
```

Verhalten:
- **1 User, 500 Logs:** alle N Worker ziehen nur seine Jobs → volle Geschwindigkeit.
- **User B kommt dazu:** B steht nach spätestens einem Durchlauf vorne; ab dann abwechselnd A, B, A, B → B wartet maximal ~1 Job-Dauer, nicht 500.
- **Optional später:** Gewichtung/Kosten (DRR) – z. B. WvW-Logs oder große Dateien „kosten“ mehr Quantum. Fürs MVP reicht reines Round-Robin.
- **Optional:** max. Jobs *gleichzeitig in Arbeit* pro User = N−1, sobald >1 User aktiv ist (verhindert, dass A alle Worker mit langen Jobs blockiert, während B kommt).

**Persistenz:** Jobs zusätzlich in **SQLite** (Status, Pfade, Hash). Beim Start werden offene Jobs wieder in die In-Memory-Queues geladen. Kein Redis/RabbitMQ nötig → weniger RAM, weniger bewegliche Teile.

**User-Identität:** Kein Login im MVP. Anonymes, signiertes Cookie / User-Token (wie dps.report `userToken`) als `UserId`; zusätzlich Rate-Limit pro IP (ASP.NET Core `RateLimiter`), damit Cookie-Wechsel kein Schlupfloch ist.

### Worker-Anzahl

Parsing ist CPU- **und** RAM-lastig. Deshalb begrenzen **zwei** Größen, wie viele Jobs gleichzeitig laufen:

- **Slots** = `MaxParallel` (hier `2`, konfigurierbar)
- **RAM-Budget** = z. B. 2,4 GB. Jeder Job hat einen geschätzten Bedarf `est = Basis + Faktor × entpackte EVTC-Größe`; Basis und Faktor kommen aus dem Benchmark (Schritt 0). Optional: pro Log-Typ (WvW höher), aus echten Messwerten der letzten Parses nachjustiert.

Ein Worker nimmt den nächsten Job (Round-Robin-Reihenfolge) nur, wenn `belegtesBudget + est ≤ Budget`. Sonst wartet er, bis RAM frei wird. Der wartende Job behält seinen Platz in der Reihenfolge, damit große Logs nicht verhungern (kein Überholen durch kleine Jobs, oder höchstens begrenzt). Implementierung: ein `SemaphoreSlim` für Slots + ein einfacher „Budget-Zähler“ unter Lock.

`SingleThreaded` ist immer `true` – laut Benchmark (1c) bringt Multi-Threading innerhalb eines Logs nur ~7 %. Startwerte für die Schätzung (aus dem VPS-Benchmark): Basis 150 MB, Faktor 42 × EVTC-MB.

Bei 2 Slots und 2 aktiven Usern bekommt jeder User faktisch „seinen“ Worker. Ein User mit 200 Logs blockiert einen neuen User höchstens, bis der nächste Slot frei wird (Dauer eines Logs). Ein späteres VPS-Upgrade ist nur eine Config-Änderung (`MaxParallel`, Budget).

---

## 4. Architektur

```
Browser ──HTTPS──► Caddy (TLS, statische Dateien, Reports direkt von Disk, brotli/gzip)
                     │
                     └─/api──► ASP.NET Core (ein Prozess)
                                 ├─ Upload-Endpoint (Streaming auf Disk, Validierung)
                                 ├─ FairScheduler + N Worker  ──► EliteInsightsAdapter ──► GW2EI*-Libs
                                 ├─ SSE-Endpoint (Live-Status je Batch)
                                 ├─ SQLite (Jobs, Reports, Hashes)
                                 └─ Cleanup-HostedService (Retention)
Disk:  /data/uploads (temporär) · /data/reports/{id}/report.html.br|.gz, report.json.gz · /data/ei-assets (CDN-Scripts)
```

### Ablauf
1. Frontend lädt mehrere Dateien hoch (pro Datei ein Request, parallel 2–3 → Fortschritt pro Datei, Retry einzeln).
2. Backend streamt auf Disk, prüft Endung + Header (`.evtc` beginnt mit `EVTC`; `.zevtc` = Zip mit genau einem Eintrag, Größe beim Entpacken begrenzen → Zip-Bomb-Schutz).
3. **SHA-256-Dedupe:** Gleicher Hash schon verarbeitet → sofort fertiges Ergebnis (kostet 0 CPU; häufig, weil Gruppenmitglieder denselben Log hochladen).
4. Job in FairScheduler; Status via **SSE** an den Browser (leichter als WebSockets/SignalR).
5. Worker parst, schreibt HTML + JSON, komprimiert vorab (nur gzip, schont HDD und CPU), löscht Upload.
6. Report-URL `/r/{kurzId}` wird von Caddy direkt ausgeliefert (`precompressed`), das Backend ist nicht beteiligt.

### Performance-Hebel (nach Wirkung sortiert)
1. In-process, warme Runtime (kein Spawn pro Log).
2. Dedupe per Hash.
3. `HtmlExternalScripts` + `HtmlExternalScriptsCdn` auf eigene `/ei-assets/<version>/` → jede HTML spart mehrere MB, JS/CSS wird vom Browser gecacht.
4. `HtmlCompressJson`, `CompressRaw`, `IndentJSON=false`, vorkomprimierte Dateien.
5. Nicht benötigte Teile deaktivierbar (z. B. `ParseCombatReplay` als Option), `DetailledWvW=false`, `SkipFailedTries` je nach Wunsch.
6. .NET-Runtime: Workstation-GC oder Server-GC mit DATAS, `GCHeapHardLimit` passend zum VPS; ReadyToRun/Tiered-PGO für schnellen Start.

---

## 5. Frontend

**Empfehlung (geändert): React 19 + TypeScript + Vite + Tailwind, als statischer Build** – gleicher UI-Stack wie voidlog, aber **ohne** Next.js-Server.

Begründung:
- **Server-Performance hängt nicht vom Framework ab.** Jedes statisch gebaute Frontend (Svelte, React, Solid, Vanilla) wird von Caddy als Datei ausgeliefert, der VPS hat damit null Laufzeit-Kosten. Die Wahl ist also reine **Wartbarkeitsfrage**.
- Die eigentliche Ergebnisseite ist die **fertige EI-HTML**. Das Frontend ist nur Upload, Fortschritt und eine Liste der Reports – klein.
- voidlog nutzt bereits React 19 + Tailwind + Radix Themes. **Ein UI-Stack für beide Projekte** = gleiche Komponenten-Muster, gleiches Styling, gleiche Lints/Configs, kein Kontextwechsel. Für KI-Unterstützung ist React außerdem das am besten abgedeckte Framework.
- Optional später: gemeinsames UI-Paket (Tailwind-Preset/Komponenten) zwischen beiden Repos.

Abgewogene Alternativen:

| Option | Pro | Contra |
|---|---|---|
| **React + Vite (Empfehlung)** | gleicher Stack wie voidlog, größtes Ökosystem, beste KI-Abdeckung | ~45 KB gz Runtime (für Nutzer irrelevant) |
| Svelte 5 + Vite | kleinste Bundles, wenig Boilerplate | zweiter Frontend-Stack neben voidlog |
| SolidJS / Preact | sehr klein, React-ähnlich | kleineres Ökosystem, kaum Vorteil hier |
| htmx + Razor (C#) | eine Sprache, kein npm | Multi-Upload mit Fortschritt pro Datei braucht trotzdem eigenes JS |
| Next.js (wie voidlog) | identisch zu voidlog | eigener Node-Server = 150–300 MB RAM, unnötig für eine Upload-Seite |
| Blazor Server | C# durchgängig | dauerhafte SignalR-Verbindung pro Tab = RAM auf dem VPS |

Svelte wäre die beste Wahl für ein **isoliertes** Projekt. Mit voidlog als Schwesterprojekt überwiegt der Vorteil eines einheitlichen Stacks.

---

## 5b. VPS-Auslastung über die Zeit beobachten

Ziel: echte Zahlen statt Schätzungen (Grundlast von voidlog, RAM-Spitzen beim Parsen), mit möglichst wenig Overhead. Empfohlene Reihenfolge:

1. **Sofort: `sysstat` (`sar`)** – `apt install sysstat`, Erfassung aktivieren. Schreibt alle 10 Minuten CPU, RAM, Swap, Disk-I/O und Netz mit; praktisch 0 Overhead. Auswertung z. B. `sar -u` (CPU), `sar -r` (RAM), `sar -d` (Disk), auch für vergangene Tage. Damit kann man schon vor dem Projektstart die Grundlast über mehrere Wochen sehen.
2. **Dashboard: [Beszel](https://github.com/henrygd/beszel)** – leichtgewichtiges Monitoring (Hub + Agent, Agent ~10–20 MB RAM), zeigt Historie für CPU, RAM, Disk, Netz **und pro Docker-Container** (voidlog vs. log-parser getrennt), mit Alarmen (z. B. RAM > 90 %, Disk voll). Hub als Container neben den anderen, über Caddy z. B. unter `status.breakbar.cc` mit Login.
3. **Anwendungs-Metriken im Parser selbst:** pro Job Dauer, Peak-RAM (`Process.PeakWorkingSet64` / GC-Infos), Dateigröße, Log-Typ in SQLite speichern. Daraus kalibriert sich die RAM-Schätzung aus Abschnitt 3 automatisch, und eine kleine Admin-Seite zeigt Queue-Länge, Wartezeiten und Logs/Stunde.

Verworfen: Prometheus + Grafana + cAdvisor (zu viel RAM für 4 GiB), Netdata (komfortabel, aber deutlich schwerer als Beszel).

---

## 6. Monorepo-Struktur

```
breakbar-log-parser/
├─ backend/
│  ├─ Breakbar.sln (oder .slnx)
│  ├─ src/
│  │  ├─ Breakbar.Api/            # ASP.NET Core Minimal API, Endpoints, SSE, Hosting
│  │  ├─ Breakbar.Processing/     # FairScheduler, Worker, EliteInsightsAdapter, Storage
│  │  └─ Breakbar.Data/           # SQLite (Dapper oder EF Core), Migrationen
│  └─ tests/
│     ├─ Breakbar.Processing.Tests/   # Scheduler-Fairness, Adapter mit Beispiel-Logs
│     └─ Breakbar.Api.Tests/
├─ frontend/                      # React 19 + Vite + TS + Tailwind (statischer Build)
│  ├─ src/
│  └─ package.json
├─ external/
│  └─ GW2-Elite-Insights-Parser/  # git submodule, gepinnt auf Release-Tag (z. B. v3.32.0.0)
├─ deploy/
│  ├─ logs.caddy                  # Site-Block, wird vom Host-Caddy per import eingebunden
│  ├─ docker-compose.yml          # caddy + api (+ Volume /data)
│  └─ breakbar.conf.example
├─ benchmark/                     # EI-Benchmark-Skripte (Windows + Linux), Logs/Ergebnisse nicht im Git
├─ testdata/                      # kleine Beispiel-Logs (git-lfs) für Tests
├─ docs/
│  └─ PLAN.md
├─ .github/workflows/             # build + test, Image bauen
├─ CLAUDE.md                      # Konventionen für KI-Unterstützung
└─ README.md
```

Wartbarkeits-Prinzipien:
- **Ein Backend-Prozess, eine DB-Datei, ein Reverse-Proxy.** Keine Message-Broker, kein Kubernetes.
- EI-Kopplung an genau einer Stelle (`EliteInsightsAdapter`) + Integrationstest mit echten Logs → EI-Update = Tag ändern, Tests laufen lassen.
- `CLAUDE.md` mit Architekturregeln, damit KI-Assistenten nicht an der Struktur vorbeibauen.
- Renovate/Dependabot für .NET- und npm-Pakete; EI-Updates manuell (Release-Notes lesen).

---

## 6a. Speicher: lokale Platte vs. Cloudflare R2

**Entscheidung: Lokale Platte fürs MVP, R2 als spätere Option hinter einer Schnittstelle.**

| | Lokale Platte (MVP) | Cloudflare R2 |
|---|---|---|
| Performance | Caddy liefert Dateien direkt aus, praktisch ohne CPU-Last; Reports werden selten gelesen, die langsame HDD stört kaum | Auslieferung über Cloudflares CDN entlastet den VPS komplett; dafür kostet jeder Report einen zusätzlichen Upload nach dem Parsen |
| Wartbarkeit | keine Zugangsdaten, kein externer Dienst, keine Netzwerkfehler | Zugangsdaten, Bucket-Setup, Fehlerbehandlung beim Upload, Content-Encoding-Feinheiten |
| Kosten | ~100 GB frei, kostenlos | 10 GB frei, danach ca. 0,015 $/GB/Monat, kein Traffic-Preis (aktuelle Preise bei Cloudflare prüfen) |
| Ausfallsicherheit | kein Backup – Plattenausfall = Reports weg | redundant gespeichert |

Gründe für die Platte zum Start:
1. Beide Ziele (Performance, Wartbarkeit) sprechen bei der zu erwartenden Last für die Platte. Die Report-Auslieferung durch Caddy kostet den VPS fast nichts.
2. **Klare Zuständigkeit:** Für Logs aus voidlog ist **voidlog** für die dauerhafte Speicherung zuständig (es nutzt R2 bereits). Der Parser liefert JSON/HTML zurück, voidlog legt sie in R2 ab. Der Parser hält diese Ergebnisse nur kurz vor (Cache, z. B. 7 Tage). Nur öffentliche Uploads auf `logs.breakbar.cc` werden länger auf der Platte gehalten (Quota, älteste zuerst löschen).
3. Rohe EVTC-Dateien werden nach dem Parsen gelöscht. Für voidlog-Logs liegen sie ohnehin schon in R2.

Für später vorbereitet:
- Speicherzugriff nur über eine Schnittstelle `IReportStore` (`LocalDiskStore` jetzt, `R2Store` später). Der Wechsel ist dann eine Config-Änderung, die übrige Codebasis bleibt gleich.
- **Wann auf R2 wechseln?** Wenn die Platte trotz Quota knapp wird, wenn Reports ein Backup brauchen oder wenn der Traffic für Reports spürbar wird.
- **Optimierung für voidlog:** Statt die Datei hochzuladen, schickt voidlog eine **vorsignierte R2-URL**, und der Parser lädt den Log selbst. Das spart einen Upload-Umweg über voidlog.
- Optional: Rohlogs öffentlicher Uploads in R2 archivieren, um sie nach EI-Updates neu parsen zu können.

---

## 6b. Deployment-Skizze mit dem Host-Caddy

Caddy läuft als Systemdienst auf dem Host und macht TLS automatisch (Let's Encrypt). Der Parser läuft als Docker-Container und lauscht nur auf Loopback.

- API-Container: `127.0.0.1:4100:8080` (nicht 4000, der gehört voidlog)
- Basis-Image: `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian, enthält **ICU**). EI setzt die Kultur `en-US` und braucht daher ICU; der Invariant-Globalization-Modus ist keine Option. Bei `-alpine`- oder `-chiseled`-Varianten nur die `-extra`-Variante bzw. mit `icu-libs` verwenden. (Gefunden beim VPS-Benchmark: ohne `libicu` bricht .NET sofort mit Exit-Code 134 ab.)
- Zweiter Linux-Stolperstein aus dem Benchmark: EIs `GW2EIParserCommons` nutzt `System.Configuration`-Benutzereinstellungen (`Settings.Default.Upgrade()`). Fehlt `~/.local/share`, stürzt EI mit `ArgumentNullException (Parameter 'path')` ab. Im eigenen Dienst daher **EI-Settings selbst als Objekte bauen** (`ParserSettings`, `HTMLSettings`, `RawFormatSettings`) und `Properties.Settings`/`ProgramHelper` aus `GW2EIParserCommons` nicht verwenden; im Container zusätzlich ein beschreibbares `HOME` mit `.local/share` vorsehen.
- Daten als **Bind-Mount** statt Named Volume, z. B. `/srv/breakbar-logs/data`, damit Caddy Reports und Frontend **direkt von der Platte** ausliefern kann. Der Benutzer `caddy` braucht Leserechte auf `reports/`, `ei-assets/` und `frontend/`.
- Die Site-Konfiguration liegt im Repo (`deploy/logs.caddy`) und wird in `/etc/caddy/Caddyfile` mit `import /srv/breakbar-logs/deploy/logs.caddy` eingebunden. voidlogs Block bleibt unberührt.

```caddy
logs.breakbar.cc {
    encode zstd gzip

    # Upload- und Status-API → .NET-Container
    handle /api/* {
        request_body {
            max_size 200MB
        }
        reverse_proxy 127.0.0.1:4100 {
            flush_interval -1          # SSE sofort durchreichen
        }
    }

    # Fertige Reports: vorkomprimiert von Disk, ohne Backend
    handle_path /r/* {
        root * /srv/breakbar-logs/data/reports
        file_server {
            precompressed gzip
        }
        header Cache-Control "public, max-age=31536000, immutable"
    }

    # EI-JS/CSS (HtmlExternalScriptsCdn zeigt hierhin), versioniert
    handle_path /ei-assets/* {
        root * /srv/breakbar-logs/data/ei-assets
        file_server
        header Cache-Control "public, max-age=31536000, immutable"
    }

    # React-Frontend (statischer Build, SPA-Fallback)
    handle {
        root * /srv/breakbar-logs/frontend
        try_files {path} /index.html
        file_server
    }
}
```

Ziel-Inhalt von `/etc/caddy/Caddyfile` (aktuell steht dort nur der voidlog-Block):

```caddy
voidlog.breakbar.cc {
        reverse_proxy localhost:4000
}

import /srv/breakbar-logs/deploy/logs.caddy
```

Änderungen prüfen und ohne Downtime laden: `caddy validate --config /etc/caddy/Caddyfile` und danach `sudo systemctl reload caddy`.

---

## 7. Umsetzungs-Schritte

| # | Schritt | Ergebnis |
|---|---|---|
| **0** | **Benchmark** – vorbereitet in [benchmark/](../benchmark/README.md) (`run-bench.ps1` für Windows, `run-bench.sh` für den VPS). Lokal (EI installieren): 5–10 typische Logs (Raid-Boss, Strike, Fractal, großer WvW-Log) mit EI-CLI parsen, Zeit + Peak-RAM messen, mit `SingleThreaded` an/aus. Danach **einmal auf dem VPS** mit der Linux-CLI wiederholen (`/usr/bin/time -v`) und `docker stats` von voidlog festhalten | Bestätigt das RAM-Budget (~2 GB) und `N = 1`, ermittelt Logs/Stunde |
| 1 | Monorepo-Skelett, Submodule, Solution baut gegen EI-Projekte | `dotnet build` grün |
| 2 | `EliteInsightsAdapter`: eine Datei → HTML + JSON (Konsolen-Spike) | Beweis: In-process funktioniert, gleiche Ausgabe wie CLI |
| 3 | FairScheduler + Worker + Tests (Fairness, work-conserving, Cancel) | Kern-Logik abgesichert |
| 4 | API: Upload, Dedupe, Jobs in SQLite, SSE-Status, Report-Auslieferung | End-to-End per curl |
| 5 | Frontend: Multi-Upload mit Fortschritt, Batch-Ansicht, Links | nutzbares MVP |
| 6 | Deploy: Dockerfile, Caddy, Volumes, Retention-Job, Rate-Limits | läuft auf dem VPS |
| 7 | Feinschliff: RAM-Schätzung aus echten Messwerten nachjustieren, DRR-Gewichtung, Metriken (`/health`, Queue-Länge) | Optimierung nach Messwerten |

---

## 8. Offene Fragen

1. ~~VPS-Daten, Subdomain, Reverse-Proxy~~ → geklärt (1b, Abschnitt 6b): Caddy auf dem Host, `logs.breakbar.cc`.
2. **Aufbewahrung:** Wie lange sollen Reports gespeichert bleiben? Vorschlag: Quota (z. B. 60 GB), älteste Reports werden zuerst gelöscht.
3. **Sichtbarkeit:** Reports öffentlich per unratbarer URL (wie dps.report) oder nur für den Uploader?
4. **Login** später nötig (z. B. Discord-OAuth für „meine Reports“) oder reicht anonymes Token?
5. **Erwartete Last:** Wie viele User gleichzeitig / Logs pro Abend?
6. Soll optional zusätzlich an dps.report hochgeladen werden (EI kann das bereits) – eher nein, um Last zu sparen?
