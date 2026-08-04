# Deploying BIT to a server (41.76.209.132)

This documents a bare-metal (no Docker) deployment of BIT to a Linux server reachable at
`41.76.209.132`, accessed over SSH, with no domain name yet (raw IP only). It's written from
what's actually in this repo today — there are no Dockerfiles, no CI/CD deploy pipeline, and no
existing deploy scripts, so every step below is a manual, repeatable command.

Three services run on the box:

| Service | What | Port (internal) |
|---|---|---|
| `dotnet-api` | .NET 8 API (EF Core/PostgreSQL, Hangfire, SignalR) | 57220 |
| `detection-service` | Python FastAPI (YOLOv11 detection) | 8001 |
| frontend | Static build (`npm run build` → `dist/`) served by nginx | — |

nginx is the only thing exposed on port 80/443; it serves the frontend's static files and
reverse-proxies `/api` and `/hubs` to `dotnet-api` on `localhost:57220` — the same split
`vite.config.ts` uses in dev. Keeping frontend + API on one public origin like this means you
never have to touch the CORS policy in `Program.cs` (which today only allows
`http://localhost:3000` and `https://*.run.app` — see "Known gaps" below).

## 0. Before you start

- SSH access to `41.76.209.132` with `sudo`.
- This guide assumes Ubuntu/Debian (`apt`). Adjust package manager commands if it's a different
  distro.
- No domain is pointed at this IP yet, so everything below runs over plain HTTP. See "Adding a
  domain + TLS later" at the end for the follow-up once you have one.
- GPU: `detection-service`'s YOLO/transformers/torch stack is CPU-capable but the repo's own
  `requirements.txt` says CPU-only is "very slow." Check `nvidia-smi` on the box; if there's no
  GPU, detection will still work, just slower — that's a capacity question for you to size, not
  something this guide can determine from here.

## 1. System packages

```bash
sudo apt update
sudo apt install -y git curl build-essential ffmpeg nginx postgresql postgresql-contrib \
  python3.12 python3.12-venv python3-pip
```

.NET 8 SDK (needed to build; you can swap to just the ASP.NET Core runtime once you're publishing
prebuilt binaries instead of building on the box):

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
sudo bash dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet
sudo ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
dotnet --version   # should print an 8.x SDK version
```

Node.js 20+ (for building the frontend):

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo -E bash -
sudo apt install -y nodejs
node --version
```

## 2. Get the code

```bash
sudo mkdir -p /opt/bit && sudo chown $USER:$USER /opt/bit
git clone <your-repo-url> /opt/bit
cd /opt/bit
```

Create a dedicated non-root user to actually run the services (don't run them as root or your own
sudo user):

```bash
sudo useradd -r -m -d /opt/bit -s /usr/sbin/nologin bitapp
sudo chown -R bitapp:bitapp /opt/bit
```

## 3. PostgreSQL

```bash
sudo -u postgres psql -c "CREATE USER bit_user WITH PASSWORD '<CHOOSE_A_REAL_PASSWORD>';"
sudo -u postgres psql -c "CREATE DATABASE afrobotics_bit OWNER bit_user;"
```

`dotnet-api/appsettings.Production.json` (checked into the repo) currently has this connection
string:

```
Host=localhost;Database=afrobotics_bit;Username=bit_user;Password=bit_password
```

**Change `bit_password` to the real password you just set** — don't deploy with the committed
default. Same file, same edit.

No manual migration step is needed beyond that: `Program.cs` calls
`context.Database.Migrate()` on every startup, so the schema is created/updated automatically the
first time `dotnet-api` runs against an empty database.

## 4. Backend: `dotnet-api`

```bash
cd /opt/bit/dotnet-api
dotnet publish -c Release -o /opt/bit/publish/api
```

Before first run, also change the JWT secret — `appsettings.json` ships with a hardcoded default
(`"Jwt": { "Secret": "AFROBOTICS_BIT_SUPER_SECRET_SECURITY_KEY_2026_JWT" }`). Override it via
environment variable rather than editing the checked-in file, so it doesn't end up back in git on
your next `git pull`:

```bash
# in the systemd unit below, or a .env-style EnvironmentFile
Jwt__Secret=<a long random string, e.g. `openssl rand -base64 48`>
```

### systemd unit

`/etc/systemd/system/bit-api.service`:

```ini
[Unit]
Description=BIT .NET API
After=network.target postgresql.service

[Service]
Type=simple
User=bitapp
WorkingDirectory=/opt/bit/publish/api
ExecStart=/usr/bin/dotnet /opt/bit/publish/api/Afrobotics.Bit.Api.dll
Restart=always
RestartSec=5
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://localhost:57220
Environment=Jwt__Secret=<the random string from above>

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now bit-api
sudo journalctl -u bit-api -f    # watch it come up, confirm "Database migrations applied successfully."
```

### First-run bootstrap: creating the initial admin account

There's no self-registration endpoint and no manual "create first admin" tool — new users are
only created by an existing Admin (`POST /api/users`, authorized-only). Seeding
(`DbSeeder.SeedInitialRecords`, which creates a starter Admin/Editor/Advertiser set) is
**skipped when `ASPNETCORE_ENVIRONMENT=Production`** (see `Program.cs`), which means a brand-new
production database has *no users at all* and nothing can log in.

One-time workaround: for the very first startup only, run with
`ASPNETCORE_ENVIRONMENT=Development` to trigger seeding, then switch back to `Production`:

```bash
sudo systemctl stop bit-api
sudo ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:57220 \
  dotnet /opt/bit/publish/api/Afrobotics.Bit.Api.dll &
# wait for "Database seeding completed." in the output, then Ctrl+C it
sudo systemctl start bit-api    # back to Production per the unit file above
```

This seeds:

| Email | Password | Role |
|---|---|---|
| `admin@afrobotics.co.za` | `admin123` | Admin |
| `loverboy.sfiso@gmail.com` | `editor123` | Editor |
| `advertiser@afrobotics.co.za` | `advertiser123` | Advertiser |

**Log in as the admin account and change its password immediately** — these are checked-into-git
credentials, not something to leave live on a public IP. Once you have a real admin, you can
delete or repurpose the other two seeded accounts from the Admin Console.

## 5. Detection service (Python)

```bash
cd /opt/bit/detection-service
python3.12 -m venv .venv
source .venv/bin/activate
pip install -r requirements.txt
deactivate
```

`/etc/systemd/system/bit-detector.service`:

```ini
[Unit]
Description=BIT Detection Service
After=network.target

[Service]
Type=simple
User=bitapp
WorkingDirectory=/opt/bit/detection-service
ExecStart=/opt/bit/detection-service/.venv/bin/uvicorn main:app --host 0.0.0.0 --port 8001
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now bit-detector
```

This only needs to be reachable from `dotnet-api` on the same box (`http://localhost:8001`) — it
does not need to be exposed through nginx.

## 6. Frontend

```bash
cd /opt/bit
npm install
npm run build      # outputs to /opt/bit/dist
```

## 7. nginx

`/etc/nginx/sites-available/bit`:

```nginx
server {
    listen 80;
    server_name 41.76.209.132;

    root /opt/bit/dist;
    index index.html;

    location / {
        try_files $uri /index.html;
    }

    location /api/ {
        proxy_pass http://localhost:57220;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        client_max_body_size 10G;   # matches UploadLimits:MaxVideoBytes in appsettings.json
    }

    location /hubs/ {
        proxy_pass http://localhost:57220;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/bit /etc/nginx/sites-enabled/bit
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t && sudo systemctl reload nginx
```

`client_max_body_size 10G` matters — the app's own Kestrel config
(`Program.cs`, `UploadLimits:MaxVideoBytes`) already allows up to 10GB broadcast file uploads;
nginx's default 1MB body limit would silently reject anything past that if you leave it unset.

At this point, `http://41.76.209.132/` should load the app.

## 8. Required post-deploy configuration (Admin Console)

Log in as the admin, go to the Admin Console's Platform Settings, and configure the pieces that
have no default and won't work until set:

**API keys** — none are set out of the box; every engine that needs one will fail with a clear
"missing configuration" error until you add it:
`falai_api_key`, `gemini_api_key`, `google_vision_api_key`, `replicate_api_key`

**Active engines** — `EngineFactory` defaults to `replicate` / `gemini` / `opencv` / `sam3` if
unset in Platform Settings; pick what you actually want to run (per `CLAUDE.md`:
`engine_detection`, `engine_brand_analysis`, `engine_compositing`, `engine_tracking`). If you want
the local YOLO service you just deployed, set `engine_detection = yolo` and `yolo_service_url =
http://localhost:8001`.

**`sam3_video_base_url`** — this is the base URL fal.ai's workers use to fetch back
locally-hosted video/image files during compositing (e.g. Pikaswaps). In dev, this pointed at a
cloudflared tunnel because a dev laptop isn't publicly reachable. **On this server you don't need
a tunnel at all** — the box has a real public IP, so set it directly:

```
sam3_video_base_url = http://41.76.209.132
```

If you skip this (or leave it pointing at `localhost`), every fal.ai-backed render will fail with
the same "can't fetch input file" error class documented in `docs/DEBUGGING_GUIDE.md` §7 — that
whole section was written for a dev-only tunnel problem that literally does not apply once you're
deployed here with a real IP.

## 9. Verifying it's actually working

```bash
curl -I http://41.76.209.132/                          # frontend
curl -I http://41.76.209.132/api/health 2>/dev/null; curl -s http://41.76.209.132/hangfire -o /dev/null -w "%{http_code}\n"  # hangfire dashboard (admin-only auth)
sudo systemctl status bit-api bit-detector nginx postgresql
```

Then in the browser: log in, ingest a short test clip, and confirm a scene detects and a render
completes — the same golden-path check this repo's own `CLAUDE.md` expects before calling a
frontend change done.

## 10. Operating it afterward

- **Logs**: `sudo journalctl -u bit-api -f`, `sudo journalctl -u bit-detector -f`, plus the app's
  own `EventLogs` table (Telemetry tab / `GET /api/logs`), per `docs/DEBUGGING_GUIDE.md`.
- **Updating**: `git pull`, re-run the `dotnet publish` / `npm run build` steps, `sudo systemctl
  restart bit-api bit-detector`, `sudo systemctl reload nginx` if the nginx config changed.
- **Storage**: uploaded video, rendered output, and temp render chunks are stored on local disk
  under `dotnet-api`'s `Uploads/` and `renders/` directories — there is no S3/object storage
  integration yet (a memory of mine notes this was requested by the client but not started).
  Monitor disk space on this box directly; broadcast-length video adds up fast.

## Known gaps this guide works around, not fixes

- **CORS** (`Program.cs`, `AllowFrontendClient` policy) only allows `http://localhost:3000` and
  `https://*.run.app`. Serving frontend + API same-origin through nginx (as above) sidesteps this
  entirely — but if you ever split them onto different origins/ports, you'll need to add
  `http://41.76.209.132` (and later your real domain) to that policy in code.
- **No production seeding path.** The Development-mode bootstrap trick in step 4 is a real gap,
  not a documented feature — there's no supported "create the first admin" flow for a fresh
  production database.
- **Hardcoded seeded credentials** (`admin123` etc.) are checked into `DbSeeder.cs`. Changing the
  admin password after first login is not optional on a publicly reachable box.

## Adding a domain + TLS later

Once you have a domain to point at `41.76.209.132`:

1. DNS: A record → `41.76.209.132`.
2. `sudo apt install certbot python3-certbot-nginx`
3. `sudo certbot --nginx -d yourdomain.example` (updates the nginx `server_name` and adds the
   HTTPS block + redirect automatically).
4. Update `sam3_video_base_url` in Platform Settings to the new `https://` domain.
5. If you ever put Cloudflare in front of the domain (proxied/orange-cloud), no further tunnel is
   needed either — Cloudflare will terminate TLS and forward to this same nginx on 80/443, same as
   any other reverse proxy.
