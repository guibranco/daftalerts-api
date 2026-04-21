# Postfix integration

DaftAlerts receives property alerts by piping incoming mail to the `DaftAlerts.EmailIngest` binary. These are the steps to wire Postfix into it on the VPS.

## Prerequisites

- Postfix installed (`apt install postfix mailutils`), configured to accept mail for your domain.
- An MX record (or alias target) that points `daft@logs.straccini.com` — or whichever address you forward alerts to — at this server.
- `DaftAlerts.EmailIngest` published as a self-contained single-file executable for `linux-x64`. From the repository root:

  ```sh
  dotnet publish src/DaftAlerts.EmailIngest/DaftAlerts.EmailIngest.csproj \
      -c Release -r linux-x64 --self-contained true \
      /p:PublishSingleFile=true /p:InvariantGlobalization=true \
      -o ./out/ingest
  ```

## Install

Copy the publish output to the server, then run:

```sh
sudo ./deploy/install.sh ./out/ingest daft
```

What this does:

1. Creates a `daftalerts` system user.
2. Installs the binary to `/opt/daftalerts/ingest/DaftAlerts.EmailIngest`.
3. Creates `/var/lib/daftalerts` (SQLite file) and `/var/log/daftalerts`.
4. Drops a `/usr/local/bin/daftalerts-ingest` wrapper script.
5. Adds `daft: "|/usr/local/bin/daftalerts-ingest"` to `/etc/aliases` and runs `newaliases`.
6. Reloads Postfix.

## Postfix configuration notes

Ensure Postfix will accept mail for your forwarding domain. In `/etc/postfix/main.cf`:

```
mydestination = $myhostname, localhost.$mydomain, localhost, logs.straccini.com
alias_maps = hash:/etc/aliases
alias_database = hash:/etc/aliases
```

The alias mechanism is what triggers the pipe. When mail arrives for `daft@logs.straccini.com`, Postfix looks up `daft` in `/etc/aliases`, sees the pipe directive, and invokes the command with the RFC 822 message on stdin. The `DaftAlerts.EmailIngest` binary:

- Reads stdin into memory.
- Parses the MIME message with MimeKit.
- Records a `RawEmail` row (deduplicated on `Message-ID`).
- Runs the parser and creates/updates a `Property` row.
- Always exits `0` — a non-zero exit would make Postfix bounce the mail.

## Forwarding mail from the user's existing inbox

If the user already receives Daft.ie alerts to a personal address, set up a server-side forward from that provider to `daft@logs.straccini.com`:

- **Gmail** → Settings → Forwarding and POP/IMAP → Add forwarding address.
- **Fastmail / Outlook** → similar "auto-forward" options.
- **Personal domain** → add an MX record or SMTP forwarder.

## Troubleshooting

- Check Postfix logs: `journalctl -u postfix -f` or `tail -f /var/log/mail.log`.
- Check DaftAlerts ingest logs: `tail -f /var/log/daftalerts/ingest-*.log`.
- Inspect raw emails stored in the `RawEmails` table of `/var/lib/daftalerts/daftalerts.db`.
- To replay a stored raw email: find its MIME bytes in the DB, write them to a file, and pipe them to the binary:
  ```sh
  cat saved.eml | sudo -u daftalerts /opt/daftalerts/ingest/DaftAlerts.EmailIngest
  ```

## Security notes

- The `daftalerts` user has no login shell.
- SQLite file is owned by `daftalerts:daftalerts`, mode `0644` (read is fine for the Api container which runs as the same UID).
- No configuration secrets are stored in the binary's directory; use environment variables on the systemd unit or docker-compose for `DaftAlerts__Auth__ApiToken` and `DaftAlerts__Geocoding__GoogleApiKey`.
