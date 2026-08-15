#!/usr/bin/env bash
# DRAFT — ACTION_PLAN E2-07. NOT executed: there is no real VPS or SSH target in this
# environment (E2-05 is explicitly out of scope for this pass). Written to the shape
# TECH_SPEC §7.5 describes; review the secret handling against whatever secret store the
# real deploy target ends up using before running this for real — it currently assumes a
# `.env` file already sitting on the VPS (git-ignored, never committed, per E2-09).
#
# Usage (on the VPS, from the repo checkout):
#   ./deploy/deploy.sh
#
# Migration design note: the runtime `api` image deliberately has NO SDK layer (E2-01),
# so `dotnet ef database update` cannot run inside it, and TECH_SPEC §7.5 says "no
# build/compile work happens on the production VPS itself" — ruling out rebuilding from
# source on the VPS just to run migrations. The standard EF Core answer to exactly this
# combination is a migrations bundle (`dotnet ef migrations bundle`): a single
# self-contained native executable, built once in CI (see .github/workflows/ci.yml —
# NOT currently wired up there; add a `dotnet ef migrations bundle --self-contained -r
# linux-musl-x64 -o efbundle` step and publish `efbundle` as a release artifact
# alongside the image tag), shipped to the VPS, and run here as the explicit,
# never-automatic migration step.
#
# E2-11 — EXTERNAL DATABASE (Neon). This script targets the bundled `db` container. To
# deploy against a managed Postgres instead, add `-f docker-compose.yml -f
# docker-compose.external-db.yml` to every `docker compose` invocation below, drop the
# "start db / wait for healthy" block (there is no container to wait on), and pass
# "$DATABASE_CONNECTION_STRING" to `./efbundle --connection` in place of the
# locally-constructed one. The migration step itself is UNCHANGED in shape: still an
# explicit, separate command, still never automatic on container start (TECH_SPEC
# §7.5, E2-07). It also resolves the caveat flagged at that step below — an external
# endpoint is reachable without temporarily publishing the db container's port.
# Left as a documented variant rather than branched logic: this script is still a
# never-executed draft and no real deploy target has been chosen (H-3 is parked).

set -euo pipefail
cd "$(dirname "$0")/.."

if [ ! -f ./efbundle ]; then
  echo "efbundle not found next to this script — build it in CI with:" >&2
  echo "  dotnet ef migrations bundle --project src/SourcingOps.Infrastructure --startup-project src/SourcingOps.Api --self-contained -r linux-musl-x64 -o efbundle" >&2
  echo "and copy it to the VPS alongside this script before deploying." >&2
  exit 1
fi
chmod +x ./efbundle

echo "==> Pulling latest images"
docker compose pull

echo "==> Starting/updating db (and caddy) first — api stays on the old image until migrated"
docker compose --profile prod up -d db caddy

echo "==> Waiting for db to report healthy"
until [ "$(docker inspect -f '{{.State.Health.Status}}' "$(docker compose ps -q db)")" = "healthy" ]; do
  sleep 2
done

echo "==> Applying pending EF Core migrations via the pre-built bundle (explicit step — never auto-applied by the api container)"
# shellcheck disable=SC1091
set -a; source .env; set +a
./efbundle --connection "Host=localhost;Port=5432;Database=sourcingops;Username=app;Password=${DB_PASSWORD}"
# NOTE: assumes db's port is reachable from the host for the duration of this command.
# The production compose file does not publish it by default (TECH_SPEC §8) — either
# temporarily publish 5432 for the migration window, or run efbundle inside a container
# attached to the compose network instead of on the bare host. Left as an explicit
# follow-up rather than guessed at, since it changes the production compose file's
# published-ports posture — flag before this script is used for a real deploy.

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
