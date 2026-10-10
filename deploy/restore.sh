#!/usr/bin/env bash
# Restores a backup made by backup.sh. Test this every month on a spare server or a copy (design doc 7.1).
#   ./restore.sh /var/backups/institutehub/institutehub-db-20261008-023000.dump.gpg [institutehub-files-....tar.gz.gpg]
# WARNING: replaces the current database (and files, if given). The app is stopped during the restore.
set -euo pipefail

cd "$(dirname "$0")"
DB_FILE="${1:?Usage: restore.sh <db backup .dump.gpg> [files backup .tar.gz.gpg]}"
FILES_FILE="${2:-}"
PASSPHRASE_FILE="${PASSPHRASE_FILE:-/root/.institutehub-backup-passphrase}"
COMPOSE="docker compose -f docker-compose.prod.yml"

decrypt() {
  gpg --batch --quiet --pinentry-mode loopback --passphrase-file "$PASSPHRASE_FILE" --decrypt "$1"
}

read -r -p "This replaces the live database with $(basename "$DB_FILE"). Type RESTORE to continue: " answer
[[ "$answer" == "RESTORE" ]] || { echo "Cancelled."; exit 1; }

$COMPOSE stop app
decrypt "$DB_FILE" | $COMPOSE exec -T postgres pg_restore -U institutehub -d institutehub --clean --if-exists --no-owner

if [[ -n "$FILES_FILE" ]]; then
  $COMPOSE start app
  decrypt "$FILES_FILE" | $COMPOSE exec -T app tar -xzf - -C /app
  $COMPOSE restart app
else
  $COMPOSE start app
fi

echo "Restore finished. Open the app, sign in and check a few students, payments and a receipt PDF."
