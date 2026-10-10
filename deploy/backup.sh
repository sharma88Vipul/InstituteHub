#!/usr/bin/env bash
# Daily encrypted backup (design doc 7.1: encrypted, kept 30 days, restore tested monthly).
# Backs up the PostgreSQL database and the app's files (logos + Data Protection keys).
#
# Install (as root on the server):
#   openssl rand -base64 32 > /root/.institutehub-backup-passphrase && chmod 600 /root/.institutehub-backup-passphrase
#   (store a copy of the passphrase in your password manager – without it backups cannot be restored)
#   crontab -e   →   30 2 * * * /opt/institutehub/deploy/backup.sh >> /var/log/institutehub-backup.log 2>&1
#
# Optional off-site copy: install rclone, configure a remote (e.g. Backblaze B2, Google Drive) and set RCLONE_REMOTE.
set -euo pipefail

cd "$(dirname "$0")"
BACKUP_DIR="${BACKUP_DIR:-/var/backups/institutehub}"
KEEP_DAYS="${KEEP_DAYS:-30}"
PASSPHRASE_FILE="${PASSPHRASE_FILE:-/root/.institutehub-backup-passphrase}"
RCLONE_REMOTE="${RCLONE_REMOTE:-}"          # e.g. b2:institutehub-backups
COMPOSE="docker compose -f docker-compose.prod.yml"

[[ -r "$PASSPHRASE_FILE" ]] || { echo "Passphrase file $PASSPHRASE_FILE not found" >&2; exit 1; }
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"
STAMP="$(date -u +%Y%m%d-%H%M%S)"

encrypt() {
  gpg --batch --yes --pinentry-mode loopback --passphrase-file "$PASSPHRASE_FILE" \
      --symmetric --cipher-algo AES256 -o "$1"
}

echo "$(date -u) backup started"

# 1. Database (custom format: compressed, restorable table by table).
DB_FILE="$BACKUP_DIR/institutehub-db-$STAMP.dump.gpg"
$COMPOSE exec -T postgres pg_dump -U institutehub -d institutehub --format=custom --no-owner | encrypt "$DB_FILE"

# 2. Uploaded files and Data Protection keys (keys keep users signed in and receipt links valid after a restore).
FILES_FILE="$BACKUP_DIR/institutehub-files-$STAMP.tar.gz.gpg"
$COMPOSE exec -T app tar -czf - -C /app data keys | encrypt "$FILES_FILE"

# Fail loudly if a dump came out suspiciously small.
for f in "$DB_FILE" "$FILES_FILE"; do
  [[ $(stat -c %s "$f") -gt 100 ]] || { echo "Backup file $f is empty" >&2; exit 1; }
done

# 3. Keep the last KEEP_DAYS days.
find "$BACKUP_DIR" -name 'institutehub-*.gpg' -mtime +"$KEEP_DAYS" -delete

# 4. Off-site copy.
if [[ -n "$RCLONE_REMOTE" ]]; then
  rclone copy "$BACKUP_DIR" "$RCLONE_REMOTE" --include 'institutehub-*-'"$STAMP"'*'
  rclone delete "$RCLONE_REMOTE" --min-age "${KEEP_DAYS}d" --include 'institutehub-*'
fi

echo "$(date -u) backup finished: $(du -h "$DB_FILE" | cut -f1) database, $(du -h "$FILES_FILE" | cut -f1) files"
