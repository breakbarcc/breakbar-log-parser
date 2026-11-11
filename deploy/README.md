# Deployment auf dem VPS

Ziel: `logs.breakbar.cc` (App und API) und `reports.breakbar.cc` (Berichte) auf dem Debian-13-VPS neben voidlog. Hintergrund und Entscheidungen: [docs/PLAN.md](../docs/PLAN.md), Abschnitte 1b, 4b, 6a und 6b.

```
Internet ──► Caddy (Host, :443, TLS) ──► 127.0.0.1:4100 ──► Container breakbar-logs (:8080)
                                                              └─ /srv/breakbar-logs/data  (SQLite, Uploads, Berichte)
```

Das Docker-Image baut GitHub Actions bei jedem Push auf `main` und legt es als `ghcr.io/breakbarcc/breakbar-log-parser:latest` ab. Der VPS lädt es nur herunter.

## Einmalige Einrichtung

### 1. DNS

A- (und ggf. AAAA-) Einträge auf die IP des VPS:

- `logs.breakbar.cc`
- `reports.breakbar.cc`

### 2. Docker-Image freigeben

Pakete einer Organisation sind auf GitHub zunächst privat. Entweder

- **öffentlich machen** (empfohlen, das Repo ist ohnehin öffentlich): GitHub → Organisation `breakbarcc` → *Packages* → `breakbar-log-parser` → *Package settings* → *Change visibility* → *Public*,
- oder auf dem VPS einmal anmelden: `docker login ghcr.io` mit einem Personal Access Token (Recht `read:packages`).

### 3. Repository und Datenordner

```bash
sudo mkdir -p /srv/breakbar-logs
sudo chown "$USER" /srv/breakbar-logs
git clone https://github.com/breakbarcc/breakbar-log-parser.git /srv/breakbar-logs
mkdir -p /srv/breakbar-logs/data
sudo chown -R 1654:1654 /srv/breakbar-logs/data   # UID des Benutzers "app" im Container
```

Das Submodule (Elite Insights) wird auf dem VPS nicht gebraucht, solange das fertige Image genutzt wird.

### 4. Secrets

```bash
cp /srv/breakbar-logs/deploy/.env.example /srv/breakbar-logs/deploy/.env
openssl rand -hex 32    # Wert als ServiceClients__ApiKeys__voidlog in deploy/.env eintragen
chmod 600 /srv/breakbar-logs/deploy/.env
```

Den gleichen Schlüssel bekommt voidlog (Header `X-Api-Key`). `deploy/.env` ist im Git ignoriert.

### 5. Starten

```bash
cd /srv/breakbar-logs
docker compose -f deploy/docker-compose.yml pull
docker compose -f deploy/docker-compose.yml up -d
docker compose -f deploy/docker-compose.yml ps        # STATUS sollte nach ~30 s "healthy" zeigen
curl -s http://127.0.0.1:4100/api/health
```

### 6. Caddy

In `/etc/caddy/Caddyfile` unter den voidlog-Block eine Zeile anhängen:

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

### 7. Kontrolle

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
- **Impressum und Datenschutz** vor dem öffentlichen Start ausfüllen (`frontend/src/pages/LegalPage.tsx`).

## Update

Nach einem Push auf `main` und grünem GitHub-Actions-Lauf:

```bash
cd /srv/breakbar-logs
git pull                                               # nur für deploy/-Dateien nötig
docker compose -f deploy/docker-compose.yml pull
docker compose -f deploy/docker-compose.yml up -d      # startet den Container neu (~5 s Unterbrechung)
docker image prune -f
```

Laufende Parses werden beim Neustart unterbrochen und danach automatisch wieder eingereiht. Ändert sich `deploy/logs.caddy`, Caddy wie oben neu laden.

**Notfall – auf eine ältere Version zurück:** In `deploy/docker-compose.yml` statt `:latest` das Tag `:sha-<commit>` eines früheren Builds eintragen und `up -d` ausführen.

## Betrieb

```bash
docker compose -f deploy/docker-compose.yml logs -f --tail 100   # Logs
docker stats breakbar-logs-app-1                                  # CPU/RAM
du -sh /srv/breakbar-logs/data/*                                  # Speicher
```

**Backup** (Datenbank im laufenden Betrieb, Berichte als Dateien):

```bash
sqlite3 /srv/breakbar-logs/data/breakbar.db ".backup '/root/breakbar-$(date +%F).db'"
rsync -a /srv/breakbar-logs/data/reports/ <backup-ziel>/reports/
```

Ohne Backup gehen bei einem Plattendefekt die Berichte öffentlicher Uploads verloren (voidlog speichert seine eigenen Ergebnisse selbst in R2).
