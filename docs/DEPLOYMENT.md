---
title: Deployment
nav_order: 3
permalink: /deployment/
description: "Step-by-step production setup on an Oracle Cloud Ubuntu VPS — Postfix SMTP piping, systemd service, Nginx reverse proxy, Let's Encrypt certificates, and Docker Compose."
---

# Deployment

Target: Ubuntu 24.04 LTS on an OCI VPS. Two viable deployment modes — pick one:

- **Mode A (recommended):** Docker Compose for the API + nginx. `DaftAlerts.EmailIngest` runs as a native binary on the host so Postfix can invoke it directly.
- **Mode B:** systemd unit for the API, native EmailIngest, nginx from the system package repository.

Both modes use the same SQLite file at `/var/lib/daftalerts/daftalerts.db`.

## Prerequisites

```sh
apt update
apt install -y postfix mailutils nginx-light certbot python3-certbot-nginx git
# Mode A only:
apt install -y docker.io docker-compose-plugin
```

## One-time host setup

```sh
# Clone somewhere stable
cd /opt
git clone https://github.com/guibranco/daftalerts.git
cd daftalerts

# Create data directories
mkdir -p /var/lib/daftalerts /var/log/daftalerts
useradd --system --home-dir /nonexistent --no-create-home --shell /usr/sbin/nologin daftalerts || true
chown -R daftalerts:daftalerts /var/lib/daftalerts /var/log/daftalerts
```

## TLS with Let's Encrypt

Issue a certificate for the API's public hostname:

```sh
certbot certonly --standalone -d daftalerts.example.com
```

Certificates land at `/etc/letsencrypt/live/daftalerts.example.com/`. Auto-renewal is handled by the `certbot.timer` systemd unit that the package installs.

## Mode A: Docker Compose

```sh
# Create a .env next to docker-compose.yml
cat >/opt/daftalerts/.env <<'EOF'
DAFTALERTS_API_TOKEN=<generate with: openssl rand -hex 32>
GOOGLE_GEOCODING_API_KEY=<your Google key, optional>
FRONTEND_ORIGIN=https://daftalerts.example.com
EOF
chmod 600 /opt/daftalerts/.env

cd /opt/daftalerts
docker compose build
docker compose up -d

# Verify
curl -s http://127.0.0.1:5080/health
```

Edit `deploy/nginx/daftalerts.conf` to set the real `server_name` and certificate paths. The compose file mounts `/etc/letsencrypt` read-only into the nginx container.

### Publish EmailIngest for Postfix (outside the container)

The ingest binary must run on the host so Postfix can invoke it.

```sh
# On a machine with .NET 10 SDK, or in a throwaway docker run
dotnet publish src/DaftAlerts.EmailIngest/DaftAlerts.EmailIngest.csproj \
    -c Release -r linux-x64 --self-contained true \
    /p:PublishSingleFile=true /p:InvariantGlobalization=true \
    -o ./out/ingest

# Copy to the server, then:
sudo ./deploy/install.sh ./out/ingest daft
```

See [deploy/postfix-setup.md](../deploy/postfix-setup.md) for details on what `install.sh` does.

## Mode B: systemd

```sh
# On a build host:
dotnet publish src/DaftAlerts.Api/DaftAlerts.Api.csproj -c Release -o ./out/api

# On the server:
install -d -o daftalerts -g daftalerts /opt/daftalerts/api
cp -r ./out/api/* /opt/daftalerts/api/
chown -R daftalerts:daftalerts /opt/daftalerts/api

# Install the .NET 10 runtime on the server
apt install -y aspnetcore-runtime-10.0

# Install env file with secrets
cat >/etc/daftalerts/api.env <<'EOF'
DaftAlerts__Auth__ApiToken=<token>
DaftAlerts__Geocoding__GoogleApiKey=<google-key>
DaftAlerts__Cors__AllowedOrigins__0=https://daftalerts.example.com
DaftAlerts__ConnectionStrings__Default=Data Source=/var/lib/daftalerts/daftalerts.db;Cache=Shared;Foreign Keys=true
DaftAlerts__Database__AutoMigrate=true
EOF
chmod 600 /etc/daftalerts/api.env

# Install the unit
cp deploy/systemd/daftalerts-api.service /etc/systemd/system/
# Edit the unit to uncomment EnvironmentFile=/etc/daftalerts/api.env
systemctl daemon-reload
systemctl enable --now daftalerts-api
systemctl status daftalerts-api

# Install the EmailIngest binary the same way as Mode A
sudo ./deploy/install.sh ./out/ingest daft
```

Configure nginx manually:

```sh
cp deploy/nginx/daftalerts.conf /etc/nginx/sites-available/daftalerts
# Edit server_name and certificate paths
ln -sf /etc/nginx/sites-available/daftalerts /etc/nginx/sites-enabled/
nginx -t && systemctl reload nginx
```

## Backup & restore

The database is a single SQLite file. Back it up hot with the `.backup` command:

```sh
sqlite3 /var/lib/daftalerts/daftalerts.db ".backup /var/backups/daftalerts-$(date +%F).db"
```

A minimal nightly backup cron:

```cron
0 4 * * * /usr/bin/sqlite3 /var/lib/daftalerts/daftalerts.db ".backup /var/backups/daftalerts-$(date +\%F).db" && find /var/backups/daftalerts-*.db -mtime +14 -delete
```

## Monitoring

- API logs: `/var/log/daftalerts/api-*.log` (rolling daily, 14 retained).
- Ingest logs: `/var/log/daftalerts/ingest-*.log` (rolling daily, 30 retained).
- Postfix logs: `/var/log/mail.log` or `journalctl -u postfix`.
- Docker logs: `docker compose logs -f api`.
- Health:
  ```sh
  curl -s https://daftalerts.example.com/health         # liveness
  curl -s https://daftalerts.example.com/health/ready   # readiness
  ```

## Updating

```sh
cd /opt/daftalerts
git pull
# Mode A:
docker compose build && docker compose up -d
# Mode B:
dotnet publish ... && systemctl restart daftalerts-api
```

Migrations run automatically on startup when `Database:AutoMigrate=true`. To run them manually instead, set `AutoMigrate=false` and run `dotnet ef database update` on the host against the prod connection string.
