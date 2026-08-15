# Deploying RecruitRank on Your Local Server

This runs all three parts (Python AI service, .NET backend, React frontend)
as Docker containers on one machine — your office server, a spare PC, or a
local VM. No cloud needed.

## 1. Install Docker on the server

**Ubuntu/Debian:**
```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
# log out and back in for the group change to apply
```

**Windows Server / Windows 10-11:** install [Docker Desktop](https://www.docker.com/products/docker-desktop/).

Check it worked:
```bash
docker --version
docker compose version
```

## 2. Copy the project to the server

Copy the whole `RecruitRank/` folder (the one with `docker-compose.yml` at
its root) onto the server — via `scp`, a USB drive, or `git clone` if it's
in a repo.

## 3. Build and start everything

From inside the `RecruitRank/` folder:

```bash
docker compose up --build -d
```

- `--build` compiles all three images the first time (takes a few minutes —
  .NET restore, npm install, and the Python embedding model download all
  happen here).
- `-d` runs it in the background so it keeps running after you close the
  terminal.

Check everything is healthy:
```bash
docker compose ps
docker compose logs -f ai_service   # watch the model finish loading
```

## 4. Open it

On the server itself: `http://localhost`
From any other machine on the same network: `http://<server-LAN-IP>`
(find the server's IP with `ip addr` on Linux or `ipconfig` on Windows)

Nginx (inside the frontend container) serves the React app on port 80 and
proxies all `/api/*` calls to the backend automatically — nobody needs to
remember separate ports for the three services.

## 5. Keep it running after a server reboot

Docker Desktop / Docker Engine restarts containers marked `restart:
unless-stopped` automatically once Docker itself starts. On Linux, make
sure the Docker service starts on boot:
```bash
sudo systemctl enable docker
```

## 6. Updating the app later

```bash
cd RecruitRank
git pull                      # or copy in your updated files
docker compose up --build -d  # rebuilds only what changed
```

## 7. Common issues

| Symptom | Fix |
|---|---|
| `ai_service` container keeps restarting | `docker compose logs ai_service` — usually the model download failed because the server had no internet on first run. It needs internet once; after that it's cached in the `model_cache` volume. |
| Frontend loads but "Find Candidates" fails | `docker compose logs backend` — check `PythonService__BaseUrl` is reaching `ai_service` (should be automatic on the Docker network, no change needed). |
| Can't reach it from other PCs on the network | Check the server's firewall allows port 80 inbound (`sudo ufw allow 80` on Ubuntu). |
| Large ZIP upload fails | Already handled — nginx and the backend are both configured for 200MB uploads. If you need more, raise `client_max_body_size` in `frontend/nginx.conf` and `RequestSizeLimit` in `SearchController.cs`, then rebuild. |

## Notes

- Everything stays on your machine — no data leaves your network, no
  external API costs (as discussed earlier, this is fully self-hosted).
- If multiple recruiters need access, they just open `http://<server-IP>`
  in their own browsers — no per-user install needed.
- For internet-facing access (not just local network), you'd additionally
  need a domain name + reverse proxy with HTTPS (e.g. via Caddy or
  Nginx+Certbot) — say the word if you need that instead of pure LAN access.
