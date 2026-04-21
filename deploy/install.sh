#!/usr/bin/env bash
# Installs the DaftAlerts EmailIngest binary on an Ubuntu host and wires up Postfix.
# Run as root (or via sudo).
#
# Usage:
#   ./deploy/install.sh <path-to-published-ingest-dir> [mail-alias]
#
# Example:
#   ./deploy/install.sh ./src/DaftAlerts.EmailIngest/bin/Release/net10.0/linux-x64/publish daft

set -euo pipefail

if [[ $EUID -ne 0 ]]; then
  echo "Must be run as root." >&2
  exit 1
fi

SRC_DIR="${1:-}"
ALIAS="${2:-daft}"

if [[ -z "$SRC_DIR" || ! -d "$SRC_DIR" ]]; then
  echo "Usage: $0 <path-to-published-ingest-dir> [mail-alias]" >&2
  exit 2
fi

INGEST_BIN="$SRC_DIR/DaftAlerts.EmailIngest"
if [[ ! -x "$INGEST_BIN" ]]; then
  echo "Expected executable at $INGEST_BIN" >&2
  exit 3
fi

INSTALL_DIR=/opt/daftalerts/ingest
DATA_DIR=/var/lib/daftalerts
LOG_DIR=/var/log/daftalerts
USER_NAME=daftalerts

echo "==> Creating user $USER_NAME (if missing)"
if ! id -u "$USER_NAME" >/dev/null 2>&1; then
  useradd --system --home-dir /nonexistent --no-create-home --shell /usr/sbin/nologin "$USER_NAME"
fi

echo "==> Creating directories"
install -d -o "$USER_NAME" -g "$USER_NAME" -m 0755 "$INSTALL_DIR"
install -d -o "$USER_NAME" -g "$USER_NAME" -m 0755 "$DATA_DIR"
install -d -o "$USER_NAME" -g "$USER_NAME" -m 0755 "$LOG_DIR"

echo "==> Copying binary"
cp -f "$SRC_DIR"/* "$INSTALL_DIR"/
chmod 0755 "$INSTALL_DIR/DaftAlerts.EmailIngest"
chown -R "$USER_NAME":"$USER_NAME" "$INSTALL_DIR"

echo "==> Wrapper script for Postfix to pipe mail"
cat >/usr/local/bin/daftalerts-ingest <<EOF
#!/bin/sh
exec $INSTALL_DIR/DaftAlerts.EmailIngest
EOF
chmod 0755 /usr/local/bin/daftalerts-ingest

echo "==> Adding Postfix alias: $ALIAS -> pipe to daftalerts-ingest"
ALIAS_LINE="${ALIAS}: \"|/usr/local/bin/daftalerts-ingest\""
if ! grep -qE "^${ALIAS}:" /etc/aliases; then
  echo "$ALIAS_LINE" >>/etc/aliases
else
  echo "Alias '$ALIAS' already present in /etc/aliases. Not modifying." >&2
fi
newaliases

echo "==> Reloading Postfix"
systemctl reload postfix || systemctl restart postfix

echo "==> Done. Mail to ${ALIAS}@<your-domain> will now be piped into DaftAlerts."
echo "    Logs:   $LOG_DIR"
echo "    Data:   $DATA_DIR"
