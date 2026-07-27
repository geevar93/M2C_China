#!/usr/bin/env bash
# DRAFT — ACTION_PLAN E2-08. NOT executed: there is no real VPS, off-box storage target,
# or cron install in this environment. Written to the shape TECH_SPEC §7.4 describes.
#
# DR-12: a backup covering only Postgres silently loses every catalog/invoice PDF, since
# the `uploads` volume is the one piece of durable state outside Postgres (TECH_SPEC
# §4.6). This script always does BOTH in one run, so a partial backup (pg_dump present,
# uploads missing) never appears as a "successful" run.
#
# Intended installation: host crontab, e.g.
#   0 2 * * * /path/to/repo/deploy/backup.sh >> /var/log/sourcingops-backup.log 2>&1
#
# Retention window and off-box destination are Open Items (TECH_SPEC OI-5) pending
# business input — BACKUP_DEST below defaults to a local directory as a placeholder;
# point it at an S3-compatible bucket / Hostinger snapshot target / rsync destination
# once OI-5 is answered, and delete this comment once that's wired up for real.

set -euo pipefail
cd "$(dirname "$0")/.."

BACKUP_DEST="${BACKUP_DEST:-./backups}"
TIMESTAMP="$(date -u +%Y%m%dT%H%M%SZ)"
RUN_DIR="${BACKUP_DEST}/${TIMESTAMP}"
mkdir -p "$RUN_DIR"

set -a; source .env; set +a

echo "==> [1/2] pg_dump"
docker compose exec -T db pg_dump -U app -Fc sourcingops > "${RUN_DIR}/sourcingops.dump"

echo "==> [2/2] uploads volume"
# Runs a throwaway container that mounts the same named volume read-only and tars it —
# does not require stopping the api container.
docker run --rm \
  -v china_m2c_uploads:/data/uploads:ro \
  -v "$(pwd)/${RUN_DIR}:/backup" \
  alpine:3 \
  tar -C /data -czf /backup/uploads.tar.gz uploads

echo "==> Verifying both artifacts are present and non-empty"
for f in "${RUN_DIR}/sourcingops.dump" "${RUN_DIR}/uploads.tar.gz"; do
  if [ ! -s "$f" ]; then
    echo "Backup verification FAILED: $f is missing or empty." >&2
    exit 1
  fi
done

echo "Backup complete: ${RUN_DIR}"
echo "TODO (OI-5): sync ${RUN_DIR} to off-box storage and apply the retention policy once the business has answered OI-5."
