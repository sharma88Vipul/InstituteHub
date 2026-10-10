# Deploying InstituteHub for a pilot institute

One small Linux server runs everything with Docker: the app, PostgreSQL, Caddy (HTTPS) and Seq (logs).
This follows design doc section 10 ("Production").

## 1. Server

- A VPS with 2 vCPU, 4 GB RAM, 40 GB disk, Ubuntu 24.04 (e.g. DigitalOcean/Hetzner/AWS Lightsail, Mumbai region).
- A domain or sub-domain, e.g. `app.yourinstitutehub.in`, with an **A record** pointing to the server's IP.
- Install Docker and the firewall:

  ```bash
  curl -fsSL https://get.docker.com | sh
  ufw allow OpenSSH && ufw allow 80 && ufw allow 443 && ufw enable
  apt-get install -y gnupg          # for encrypted backups
  ```

## 2. First start

```bash
mkdir -p /opt/institutehub && cd /opt/institutehub
git clone https://github.com/<you>/InstituteHub.git .      # or copy the deploy folder + Dockerfile
cd deploy
cp .env.example .env && nano .env                           # domain, passwords, platform admin
docker compose -f docker-compose.prod.yml up -d --build      # or "pull" when CI publishes the image
docker compose -f docker-compose.prod.yml logs -f app        # wait for "Now listening on"
```

Open `https://<your domain>` – Caddy fetches the HTTPS certificate on the first request.
Sign in with the platform admin from `.env` to see `/hangfire`; institutes sign up at `/signup`.

## 3. Updating to a new version

```bash
cd /opt/institutehub && git pull
cd deploy
docker compose -f docker-compose.prod.yml build app          # or: pull app (image from CI)
# Database changes: either keep Database__MigrateOnStartup=true in .env, or run the bundle first:
set -a && . ./.env && set +a                                 # makes $POSTGRES_PASSWORD available
docker compose -f docker-compose.prod.yml run --rm --entrypoint ./efbundle app \
    --connection "Host=postgres;Database=institutehub;Username=institutehub;Password=$POSTGRES_PASSWORD"
docker compose -f docker-compose.prod.yml up -d app
```

Take a backup (`./backup.sh`) before any update that contains a migration.

## 4. Backups (daily, encrypted, 30 days)

```bash
openssl rand -base64 32 > /root/.institutehub-backup-passphrase
chmod 600 /root/.institutehub-backup-passphrase
# Save this passphrase in your password manager – backups cannot be restored without it.
chmod +x backup.sh restore.sh
./backup.sh                                                   # first run by hand
crontab -e
# 30 2 * * * /opt/institutehub/deploy/backup.sh >> /var/log/institutehub-backup.log 2>&1
```

- Backups go to `/var/backups/institutehub` (database dump + uploaded files and keys, AES-256 encrypted).
- Keep an **off-site copy**: install `rclone`, configure a remote (Backblaze B2, Google Drive, S3) and add
  `RCLONE_REMOTE=b2:institutehub-backups` in front of the cron command.
- **Test a restore every month** on a spare server: `./restore.sh <db file> <files file>`.

## 5. Monitoring

| What | How |
| --- | --- |
| Is the site up? | Free uptime monitor (UptimeRobot / Better Stack) on `https://<domain>/health` every 5 minutes, alert by email + WhatsApp/SMS. It returns 503 when the database or the job server is down. |
| Errors and logs | Seq: `ssh -L 5341:localhost:5341 you@server`, then open http://localhost:5341. Filter by `TenantId` to look at one institute. Add an alert in Seq for `@Level = 'Error'`. |
| Background jobs | `/hangfire` (platform admin): failed reminder/due jobs, retries. `/health` reports "Degraded" when more than 20 jobs failed. |
| Server | `docker stats`, `df -h` (keep 30 % disk free), automatic security updates: `apt install unattended-upgrades`. |
| Backups | Check `/var/log/institutehub-backup.log` weekly; restore test monthly. |

Health endpoints: `/health/live` (process is up – used by Docker), `/health/ready` and `/health`
(database + background jobs, JSON).

## 6. Pilot checklist

- [ ] `.env` filled with strong passwords; `.env` not committed.
- [ ] HTTPS works, `/health` shows `Healthy`.
- [ ] Platform admin can open `/hangfire`; the daily jobs `monthly-dues` and `fee-reminders` are listed.
- [ ] Backup cron installed, first backup file exists, passphrase stored safely, a restore was tested.
- [ ] Uptime monitor and Seq error alert set up.
- [ ] Institute owner signed up, completed the setup checklist (`/onboarding`): details + logo, fee plan, batch,
      students imported from CSV, staff and teachers added.
- [ ] A test fee collected and its receipt PDF printed; then cancelled.
- [ ] WhatsApp provider: until one is connected (`Messaging__Provider=Fake`), messages are only logged – tell the
      institute that reminders are not sent to parents yet.
