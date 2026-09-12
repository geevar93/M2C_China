#!/usr/bin/env bash
# DRAFT — ACTION_PLAN E2-07. NOT executed: there is no real VPS or SSH target in this
# environment (E2-05 is explicitly out of scope for this pass). Written to the shape
# TECH_SPEC §7.5 describes; review the secret handling against whatever secret store the
# real deploy target ends up using before running this for real — it currently assumes a
# `.env` file already sitting on the VPS (git-ignored, never committed, per E2-09), and
# a full repo checkout at the compose project root (for `docker compose build migrator`).
#
# Usage (on the VPS, from the repo checkout):
#   ./deploy/deploy.sh
#
# Migration design note: the runtime `api` image deliberately has NO SDK layer (E2-01),
# so `dotnet ef database update`/`dotnet ef migrations bundle` can't run inside IT. The
# migration step instead runs from `src/SourcingOps.Migrator` (mirrors
# KlaraHome.Migrator) — its own small image with no SDK either, built here from source
# and run once via the `migrate` compose profile, attached to the same network as `db`
# so no port needs publishing. Still an explicit, separate, never-automatic step
# (TECH_SPEC §7.5, E2-07) — building this one small image on the VPS is a different
# tradeoff than rebuilding the whole `api` image there, which this deploy still avoids.
#
# E2-11 — EXTERNAL DATABASE (Neon). This script targets the bundled `db` container. To
# deploy against a managed Postgres instead, add `-f docker-compose.yml -f
# docker-compose.external-db.yml` to every `docker compose` invocation below, drop the
# "start db / wait for healthy" block (there is no container to wait on), and set
# `ConnectionStrings__Default` to `$DATABASE_CONNECTION_STRING` for the migrator run in
# place of the locally-constructed one. The migration step itself is UNCHANGED in shape:
# still explicit, still never automatic on container start (TECH_SPEC §7.5, E2-07).
# Left as a documented variant rather than branched logic: this script is still a
# never-executed draft and no real deploy target has been chosen (H-3 is parked).

set -euo pipefail
cd "$(dirname "$0")/.."

echo "==> Pulling latest images"
docker compose pull

echo "==> Starting/updating db (and caddy) first — api stays on the old image until migrated"
docker compose --profile prod up -d db caddy

echo "==> Waiting for db to report healthy"
until [ "$(docker inspect -f '{{.State.Health.Status}}' "$(docker compose ps -q db)")" = "healthy" ]; do
  sleep 2
done

echo "==> Building and running the migrator (explicit step — never auto-applied by the api container)"
docker compose --profile migrate build migrator
docker compose --profile migrate run --rm migrator

echo "==> Starting api on the new image"
docker compose --profile prod up -d api

echo "==> Post-deploy smoke check (E2-10) — fails the deploy if unhealthy"
for i in $(seq 1 10); do
  if curl -fsS http://localhost:5000/api/v1/health | grep -q '"status":"Healthy"'; then
    echo "Smoke check passed."
    exit 0
  fi
  sleep 3
done

echo "Smoke check FAILED — health endpoint did not report healthy after deploy." >&2
exit 1
