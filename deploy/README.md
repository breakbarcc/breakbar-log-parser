# Deployment auf dem VPS

Ziel: `logs.breakbar.cc` (App und API) und `reports.breakbar.cc` (Berichte) auf dem Debian-13-VPS neben voidlog. Hintergrund und Entscheidungen: [docs/PLAN.md](../docs/PLAN.md), Abschnitte 1b, 4b, 6a und 6b.

```
git push origin main:prod
   │
   ▼  GitHub Actions (.github/workflows/deploy.yml)
   │  1. Tests (ci.yml)          3. docker save | gzip | ssh vps 'gunzip | docker load'
   │  2. Image bauen             4. deploy.sh auf dem VPS: starten, auf "healthy" warten, alte Images aufräumen
   ▼
Internet ──► Caddy (Host, :443, TLS) ──► 127.0.0.1:4100 ──► Container breakbar-logs (:8080)
                                                              └─ /srv/breakbar-logs/data  (SQLite, Uploads, Berichte)
```

Das Image wird auf GitHub gebaut und direkt per SSH auf den VPS geladen. Es gibt **keine Registry** und **keinen Build auf dem VPS** (2 vCPU, geteilt mit voidlog). Auf dem VPS liegt kein Quellcode: nur `deploy/` (Compose-Datei, Caddy-Datei, Skript), `data/` und die geheime `.env`.

Das Muster entspricht dem von voidlog (Branch `prod`, geschützte Umgebung `prod`, SSH als Benutzer `deploy`), nur ohne Build auf dem Server.

## Einmalige Einrichtung

### 1. DNS

A- (und ggf. AAAA-) Einträge auf die IP des VPS:

- `logs.breakbar.cc`
- `reports.breakbar.cc`

Bei Cloudflare als **DNS only (graue Wolke)** anlegen, nicht als Proxy (Plan, Abschnitt 4d).

### 2. Ordner und Benutzer auf dem VPS

Der Benutzer `deploy` existiert dort schon (voidlog nutzt ihn) und darf Docker verwenden. Prüfen: `id deploy` muss die Gruppe `docker` enthalten. Dann:

```bash
sudo mkdir -p /srv/breakbar-logs/deploy /srv/breakbar-logs/data
sudo chown -R deploy:deploy /srv/breakbar-logs
sudo chown 1654:1654 /srv/breakbar-logs/data   # UID des Benutzers "app" im Container
```

### 3. SSH-Schlüssel nur für dieses Repo

Auf deinem Rechner (nicht auf dem VPS):

```bash
ssh-keygen -t ed25519 -f breakbar-deploy -C "github-actions breakbar-logs" -N ""
ssh-copy-id -i breakbar-deploy.pub deploy@<vps>      # oder den Inhalt von breakbar-deploy.pub in ~deploy/.ssh/authorized_keys eintragen
ssh-keyscan -t ed25519 <vps>                         # Ausgabe wird gleich als VPS_HOST_KEY gebraucht
```

Die private Datei `breakbar-deploy` gehört danach **nur** noch in das GitHub-Secret und wird vom Rechner gelöscht.

### 4. Geheime Einstellungen auf dem VPS (`.env`)

```bash
echo "ServiceClients__ApiKeys__voidlog=$(openssl rand -hex 32)" > /srv/breakbar-logs/deploy/.env
chmod 600 /srv/breakbar-logs/deploy/.env
cat /srv/breakbar-logs/deploy/.env        # den Schlüssel auch in voidlog eintragen (Header X-Api-Key)
```

Weitere Optionen stehen in [.env.example](.env.example). Die `.env` bleibt auf dem VPS, wird nie überschrieben und ist im Git ignoriert.

### 5. GitHub: Umgebung `prod`

Repository → *Settings* → *Environments* → *New environment* `prod`:

| Typ | Name | Wert |
|---|---|---|
| Secret | `VPS_SSH_KEY` | Inhalt der privaten Datei `breakbar-deploy` |
| Variable | `VPS_HOST` | Hostname oder IP des VPS |
| Variable | `VPS_HOST_KEY` | Ausgabe von `ssh-keyscan -t ed25519 <vps>` (pinnt den Server) |
| Variable (optional) | `VPS_USER` | Standard `deploy` |
| Variable (optional) | `VPS_PORT` | Standard `22` |
| Variable (optional) | `VPS_APP_DIR` | Standard `/srv/breakbar-logs` |

Schutz (wie bei voidlog, das Repo ist öffentlich):

- In der Umgebung *Deployment branches* auf **`prod`** beschränken.
- *Settings → Branches*: Branch `prod` schützen, nur du darfst pushen.
- Der Deploy-Workflow läuft bewusst **nicht** bei Pull Requests, damit fremde Forks nie an das SSH-Secret kommen.

### 6. Erstes Deployment

```bash
git push origin main:prod
```

Fortschritt im Actions-Tab. Danach auf dem VPS (Status sollte nach ~30 s „healthy“ sein):

```bash
cd /srv/breakbar-logs
docker compose -f deploy/docker-compose.yml ps
curl -s http://127.0.0.1:4100/api/health
```

### 7. Caddy

Nach dem ersten Deployment liegt `/srv/breakbar-logs/deploy/logs.caddy` auf dem VPS. In `/etc/caddy/Caddyfile` unter den voidlog-Block eine Zeile anhängen:

```caddy
voidlog.breakbar.cc {
        reverse_proxy localhost:4000
}

import /srv/breakbar-logs/deploy/logs.caddy
```

Prüfen und ohne Ausfall neu laden:

```bash
caddy validate --config /etc/caddy/Caddyfile
sudo systemctl reload caddy
```

### 8. Kontrolle

```bash
curl -s https://logs.breakbar.cc/api/health
curl -sI https://logs.breakbar.cc/api/jobs | grep -i set-cookie        # muss "secure" und "httponly" enthalten
curl -sI https://logs.breakbar.cc/r/AbCdEfGhIj | grep -i location      # Weiterleitung auf reports.breakbar.cc
curl -s -o /dev/null -w "%{http_code}\n" https://reports.breakbar.cc/api/health   # 404 (keine API auf der Berichts-Domain)
```

Dann im Browser `https://logs.breakbar.cc` öffnen und einen Log hochladen.

## Empfohlen

- **voidlog begrenzen:** In voidlogs Compose-Datei `mem_limit` setzen (z. B. 1 GB für alle Dienste zusammen), damit sich die beiden Projekte nicht gegenseitig den Speicher wegnehmen.
- **Swap** als Notreserve, falls noch keiner aktiv ist (`swapon --show`):
  ```bash
  sudo fallocate -l 3G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
  echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab
  ```
- **Datenschutz:** Die Datenschutzerklärung (`frontend/src/pages/PrivacyPage.tsx`, Plan Abschnitt 4c) verspricht **höchstens 7 Tage** Speicherung und **keine Sicherungskopien** von Berichten und Upload-Daten. Der Dienst löscht selbst (`Retention__ReportDays`, Standard 7); erhöhe den Wert nie, ohne die Erklärung zu ändern. Das Impressum liegt auf `www.breakbar.cc/de/impressum`.

## Update

```bash
git push origin main:prod        # oder main in prod mergen
```

Der Workflow testet, baut, überträgt das Image (komprimiert grob 150 MB) und startet den Container neu (~5 s Unterbrechung). Schlägt irgendein Schritt vor dem Start fehl (Tests, Build, Übertragung), läuft die alte Version unberührt weiter. Laufende Parses werden beim Neustart unterbrochen und danach automatisch wieder eingereiht.

- Ändert sich `deploy/logs.caddy`: die Datei kommt mit dem Deployment auf den VPS, Caddy musst du aber von Hand neu laden (`sudo systemctl reload caddy`).
- Auf dem VPS bleiben die letzten 3 Images für Rollbacks, ältere löscht `deploy.sh`.

**Notfall – auf eine ältere Version zurück** (auf dem VPS):

```bash
docker images breakbar-logs                         # Tags = Commit-Hashes, neueste zuerst
cd /srv/breakbar-logs
IMAGE_TAG=<commit-hash> bash deploy/deploy.sh
```

Danach `prod` auf einen guten Commit zurücksetzen, sonst überschreibt das nächste Deployment den Rollback.

## Betrieb

```bash
docker compose -f deploy/docker-compose.yml logs -f --tail 100   # Logs (im Ordner /srv/breakbar-logs)
docker stats breakbar-logs-app-1                                  # CPU/RAM
du -sh /srv/breakbar-logs/data/*                                  # Speicher
```

**Backup: bewusst keines für `data/`.** Berichte und Upload-Daten sind flüchtig (höchstens 7 Tage, siehe Datenschutzerklärung, die Sicherungskopien ausschließt). Bei einem Plattendefekt gehen nur die Berichte der letzten Tage verloren; Logs lassen sich neu hochladen, und voidlog speichert seine eigenen Ergebnisse selbst in R2. Sichere stattdessen nur, was nicht flüchtig ist: `deploy/.env` (API-Key) und die Konfiguration im Git.

Achtung bei Snapshots des Hosters (z. B. des gesamten VPS): Sie enthalten auch `data/` und widersprechen der Aussage „keine Sicherungskopien“, wenn sie länger als 7 Tage aufbewahrt werden. Entweder automatische Snapshots auf 7 Tage begrenzen oder den Satz in der Datenschutzerklärung anpassen.
