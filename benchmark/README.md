# Elite-Insights-Benchmark

Misst, wie viel **Zeit** und **RAM** Elite Insights (EI) pro Log braucht und ob **zwei Logs parallel** auf dem VPS mehr Durchsatz bringen als einer. Die Ergebnisse legen fest, wie viele Logs gleichzeitig laufen und wie groß das RAM-Budget ist (siehe [docs/PLAN.md](../docs/PLAN.md), Abschnitte 1b und 3).

## Was gemessen wird

Jeder Log wird in einem **eigenen EI-Prozess** verarbeitet, wie später ein Worker-Job. Es gibt drei Durchläufe über alle Logs:

| Batch | Gleichzeitig | `SingleThreaded` | Frage |
|---|---|---|---|
| **A** | 1 | `false` | Wie schnell ist ein Log, wenn er alle Kerne bekommt? |
| **B** | 1 | `true` | Wie schnell ist ein Log auf einem Kern? (Vergleichswert) |
| **C** | 2 | `true` | Schaffen zwei parallele Logs mehr pro Stunde als A? Wie viel RAM brauchen beide zusammen? |

Die Einstellungen entsprechen der geplanten Produktion ([ei-bench.template.conf](ei-bench.template.conf)): HTML mit externen Skripten und komprimiertem JSON, JSON-Export als gzip, keine Uploads, Combat Replay an. Mit `-NoCombatReplay` bzw. `--no-combat-replay` lässt sich vergleichen, wie viel das Combat Replay kostet.

**Ergebnisdateien** in `results/<rechner>-<zeitstempel>/`:
- `per-log.csv` – pro Log: Laufzeit, EI-interne Zeit (`ei_elapsed_ms`), Spitzen-RAM, Größe von Log, entpacktem EVTC, HTML und JSON
- `batches.csv` – pro Batch: Gesamtzeit, Logs pro Stunde, RAM-Spitze aller gleichzeitig laufenden Prozesse
- `system.txt` – CPU, Kerne, RAM, EI-Version; auf dem VPS zusätzlich `docker-stats-before.txt` (Last von voidlog)

Die Zeit pro Log enthält den Start von .NET (~0,5–1 s). Der spätere Dienst hat diesen Start nicht, weil er EI im laufenden Prozess nutzt. `ei_elapsed_ms` zeigt die reine Parse-Zeit.

## 1. Test-Logs auswählen

In den Ordner `benchmark/logs/` (wird nicht ins Git eingecheckt) **8–15 Logs** legen, die typisch sind, plus Extremfälle:

- 3–4 Raid-Bosse (kurz und lang, z. B. ein schneller Kill und ein langer Wipe)
- 2–3 Strikes / Fractal-CMs
- 1–2 Golem-/Trainingslogs (klein)
- **1–2 große WvW-Logs** (Zerg-Kämpfe – der wichtigste Test für das RAM-Limit)
- der **größte Log**, den du findest

Sinnvolle Dateinamen helfen bei der Auswertung, z. B. `raid-w4-cairn-kill.zevtc`, `wvw-zerg-large.zevtc`.

## 2. Lokal unter Windows

1. **.NET 10 Runtime** installieren (falls nicht vorhanden): <https://dotnet.microsoft.com/en-us/download/dotnet/10.0> → „ASP.NET Core Runtime 10.0“ (x64).
2. **EI-CLI** herunterladen: `GW2EICLI.zip` aus dem neuesten Release von <https://github.com/baaron4/GW2-Elite-Insights-Parser/releases> und in einen eigenen Ordner entpacken, z. B. `C:\Tools\GW2EICLI\`.
3. In PowerShell im Ordner `benchmark` starten (Pfad zur `.exe` oder `.dll` in der Zip angeben):

   ```powershell
   .\run-bench.ps1 -EiPath C:\Tools\GW2EICLI\GuildWars2EliteInsights-CLI.exe
   ```

   Falls Windows das Skript blockiert, einmalig für diese Sitzung erlauben: `Set-ExecutionPolicy -Scope Process Bypass`.

   Optionen: `-Repeat 2` (alles zweimal, gleicht Ausreißer aus), `-Batches A,C` (nur bestimmte Batches), `-NoCombatReplay`, `-LogDir <Ordner>`.

## 3. Auf dem VPS (Debian 13)

Das ist der entscheidende Lauf, weil nur er die echte Hardware und die Last von voidlog zeigt. Alles wird im Home-Verzeichnis installiert und lässt sich danach rückstandslos löschen.

1. Ordner und Dateien auf den VPS kopieren (vom Windows-Rechner aus):

   ```bash
   scp -r benchmark pom@<vps>:~/ei-bench
   ```

2. Auf dem VPS GNU `time`, `unzip` und die Unicode-Bibliothek ICU installieren (`libicu76` auf Debian 13; ohne sie bricht .NET sofort mit Exit-Code 134 ab) und die .NET-10-Runtime **nur für deinen Benutzer** installieren (Microsofts offizielles Installationsskript):

   ```bash
   sudo apt install time unzip libicu76
   curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
   bash dotnet-install.sh --channel 10.0 --runtime aspnetcore
   ```

3. EI-CLI herunterladen und entpacken:

   ```bash
   cd ~/ei-bench
   curl -sSL -o GW2EICLI.zip https://github.com/baaron4/GW2-Elite-Insights-Parser/releases/latest/download/GW2EICLI.zip
   unzip -o GW2EICLI.zip -d ei
   ```

4. Starten (läuft mit `nice 10` wie später in Produktion; dauert je nach Logs einige Minuten):

   ```bash
   chmod +x run-bench.sh
   ./run-bench.sh ei/GuildWars2EliteInsights-CLI.dll
   ```

   Optionen: `--repeat 2`, `--batches A,C`, `--no-combat-replay`, `--nice 0`.

5. Aufräumen, wenn alles übertragen ist:

   ```bash
   rm -rf ~/ei-bench ~/.dotnet ~/dotnet-install.sh
   ```

> Wenn ein großer WvW-Log den VPS an die RAM-Grenze bringt, kann der Linux-OOM-Killer einen Prozess beenden. Den Benchmark daher nicht zur Hauptnutzungszeit von voidlog starten und ggf. vorher prüfen, ob Swap aktiv ist (`swapon --show`).

## 4. Ergebnisse zurückgeben

Die drei Dateien `per-log.csv`, `batches.csv` und `system.txt` (vom VPS zusätzlich `docker-stats-before.txt`) hier in den Chat kopieren oder unter `benchmark/results/` ablegen. Daraus werden abgeleitet:

- **`MaxParallel`**: Ist `logs_per_hour` in Batch C deutlich höher als in A, laufen 2 Logs parallel.
- **RAM-Formel** `est = Basis + Faktor × evtc_bytes`: lineare Näherung über `peak_rss_mb` gegen `evtc_bytes`, getrennt für WvW und PvE.
- **RAM-Budget** und Container-Limit: aus der höchsten Summe in Batch C plus Sicherheitsabstand.
- **Speicher pro Report** (`out_total_bytes`) für die Speicher-Quota.

Unverifiziert: Ob `GW2EICLI.zip` eine `.exe` enthält oder nur die `.dll` – beides funktioniert mit den Skripten (`.dll` wird über `dotnet` gestartet).
