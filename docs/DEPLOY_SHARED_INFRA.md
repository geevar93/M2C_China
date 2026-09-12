# Deploying to the VPS: shared Postgres, Redis, MinIO + shared Caddy, new app resources

This is a variant of the standard `docker-compose.yml --profile prod` deploy. It does
**not** start the bundled `db`, `redis`, `minio`, or `caddy` services. Instead:

- The app gets a brand-new database (`sourcingops`) created inside the **existing**
  shared Postgres container (`klarahome-postgres`). No new DB container, no new volume
  — it lives in that container's existing data volume.
- The app uses the **existing** shared Redis container (`klarahome-redis`) for caching,
  authenticated with its existing password. No new Redis container.
- The app gets a brand-new bucket + scoped credentials inside the **existing** shared
  MinIO container (`klarahome-minio`). No new MinIO container, no new volume.
- The app is fronted by the **existing** Caddy container (network `web`) via a new
  site block added to its Caddyfile. No new Caddy container.
- Only the `api` container is new for this app, and it joins the shared `klarahome-data`
  network (to reach Postgres/Redis/MinIO) and the `web` network (to be reached by Caddy).

Confirmed environment values (from VPS inspection via `docker ps` / `docker inspect`):

| Item | Value |
|---|---|
| Postgres container | `klarahome-postgres` (aliases: `postgres`, `klarahome-postgres`) |
| Redis container | `klarahome-redis` (aliases: `redis`, `klarahome-redis`) |
| MinIO container | `klarahome-minio` (aliases: `minio`, `klarahome-minio`) |
| Shared backend network | `klarahome-data` (Postgres, Redis, and MinIO are all on it) |
| Caddy container | `caddy` |
| Caddy network | `web` |
| Postgres superuser | `klarahome` |
| New database name | `sourcingops` |
| New app DB role | `app` |
| New MinIO bucket | `sourcingops` |
| New MinIO access key | scoped IAM user, created in step 3 below (not the MinIO root user) |

Note: `klarahome-minio` also has `9000`/`9001` published to the host and sits on the
`web` network in addition to `klarahome-data`. Neither of those facts is used by this
deploy — the api reaches it over `klarahome-data` at `http://klarahome-minio:9000` just
like Postgres and Redis, so nothing here needs the published host ports or the `web`
attachment. Don't route the app's S3 traffic through the public ports.

---

## 1. Create the database and role inside the existing Postgres container

```bash
docker exec -it klarahome-postgres psql -U klarahome -c \
  "CREATE ROLE app WITH LOGIN PASSWORD '<strong-password>';"

docker exec -it klarahome-postgres psql -U klarahome -c \
  "CREATE DATABASE sourcingops OWNER app;"

# Verify
docker exec -it klarahome-postgres psql -U klarahome -c "\l" | grep sourcingops
docker exec -it klarahome-postgres psql -U klarahome -c "\du" | grep app
```

Generate the password with `openssl rand -base64 24` and store it — you'll put it in
`.env` in step 4. No new volume is created; `sourcingops` lives inside
`klarahome-postgres`'s existing data volume alongside its other databases.

---

## 2. Get the shared Redis password

`klarahome-redis` requires auth (`requirepass`) — this app must authenticate with the
**same** password already used by the other apps sharing that container (there's no
separate role/ACL model here the way Postgres has roles or MinIO has IAM users; Redis's
`requirepass` is a single shared secret for the whole instance).

Retrieve it from a service that already connects successfully, e.g.:

```bash
docker exec klarahome-api env | grep -i redis
# or, if it's only in that stack's compose file / .env on the VPS:
grep -i redis /opt/sites/<klarahome-app-dir>/.env
```

Store the value — you'll put it in `.env` in step 4 as part of the connection string.
Because this is a shared secret with no per-app scoping, treat it with the same care as
the Postgres superuser password: don't log it, don't put it in a place other than this
app's git-ignored `.env`.

---

## 3. Create a scoped MinIO bucket and access key

Rather than reusing the MinIO root credentials (which can read/write every bucket for
every app on this instance), create a dedicated bucket and an IAM user restricted to
just that bucket — mirroring the Postgres pattern of a new DB + new role instead of
reusing the superuser.

Run these against the live container with its bundled `mc` client:

```bash
# Point mc at the local MinIO using the root credentials (one-time alias, not stored
# anywhere persistent — mc keeps it in the container's own state dir).
docker exec klarahome-minio mc alias set local http://localhost:9000 <minio-root-user> <minio-root-password>

# Create the bucket for this app.
docker exec klarahome-minio mc mb local/sourcingops

# Create a policy scoped to only that bucket.
cat <<'EOF' > /tmp/sourcingops-policy.json
{
  "Version": "2012-10-17",
  "Statement": [
    {
      "Effect": "Allow",
      "Action": ["s3:*"],
      "Resource": ["arn:aws:s3:::sourcingops", "arn:aws:s3:::sourcingops/*"]
    }
  ]
}
EOF
docker cp /tmp/sourcingops-policy.json klarahome-minio:/tmp/sourcingops-policy.json
docker exec klarahome-minio mc admin policy create local sourcingops-rw /tmp/sourcingops-policy.json

# Create a dedicated access key/secret for this app and attach the policy.
docker exec klarahome-minio mc admin user add local <sourcingops-access-key> <sourcingops-secret-key>
docker exec klarahome-minio mc admin policy attach local sourcingops-rw --user <sourcingops-access-key>

# Verify: this user should see only its own bucket.
docker exec klarahome-minio mc alias set sourcingops-check http://localhost:9000 <sourcingops-access-key> <sourcingops-secret-key>
docker exec klarahome-minio mc ls sourcingops-check
```

Generate `<sourcingops-access-key>` / `<sourcingops-secret-key>` the same way as the
Postgres password (`openssl rand -hex 16` / `openssl rand -base64 24` work fine — MinIO
has no format requirement beyond non-empty). Store both — they go in `.env` in step 4 as
`MINIO_ROOT_USER` / `MINIO_ROOT_PASSWORD` (the api's config keys are named after the
bundled-MinIO case, but here they hold this app's scoped credentials, not the shared
instance's actual root credentials).

Since the bucket already exists and this credential owns it, leave
`Storage__S3__CreateBucketIfMissing` at its default — the app's own bucket-create-if-
missing startup check will simply see the bucket already there and no-op (it doesn't
need permission to create *other* buckets, only to use this one).

---

## 4. Add a compose override for the shared-infra topology

Create a **new** file `docker-compose.shared-infra.yml` at the repo root (do not edit
the base `docker-compose.yml` — this mirrors the pattern `docker-compose.external-db.yml`
already uses for pointing at a database outside this compose project):

```yaml
services:
  api:
    environment:
      - ConnectionStrings__Default=${DATABASE_CONNECTION_STRING:?set in .env}
      - Caching__Provider=Redis
      - Caching__Redis__ConnectionString=${REDIS_CONNECTION_STRING:?set in .env}
      - Storage__Provider=S3
      - Storage__S3__ServiceUrl=${STORAGE_S3_SERVICE_URL:-http://klarahome-minio:9000}
    depends_on: !reset null
    networks:
      - default
      - klarahome-data
      - web

  db: !reset null
  redis: !reset null
  minio: !reset null
  caddy: !reset null

networks:
  klarahome-data:
    external: true
  web:
    external: true

volumes:
  pgdata: !reset null
  caddy-data: !reset null
  caddy-config: !reset null
  miniodata: !reset null
```

This drops the bundled `db`, `redis`, `minio`, and `caddy` services and their volumes
entirely, and attaches `api` to both existing external networks. Note the compose
`environment` block above pins `Caching__Provider` and `Storage__Provider` directly
(rather than only via `.env`'s `CACHING_PROVIDER` / `STORAGE_PROVIDER`) so this override
can't accidentally be run with the bundled-container defaults still active.

---

## 5. Set up `.env` on the VPS

Copy `.env.example` to `.env` (git-ignored) in the repo checkout on the VPS, then set:

```
DATABASE_CONNECTION_STRING=Host=klarahome-postgres;Port=5432;Database=sourcingops;Username=app;Password=<strong-password-from-step-1>
REDIS_CONNECTION_STRING=klarahome-redis:6379,password=<redis-password-from-step-2>
STORAGE_S3_SERVICE_URL=http://klarahome-minio:9000
STORAGE_S3_BUCKET=sourcingops
MINIO_ROOT_USER=<sourcingops-access-key-from-step-3>
MINIO_ROOT_PASSWORD=<sourcingops-secret-key-from-step-3>
JWT_SIGNING_KEY=<output of: openssl rand -base64 48>
BOOTSTRAP_ADMIN_EMAIL=<real owner email — do not use owner@sourcingops.local>
BOOTSTRAP_ADMIN_PASSWORD=<a strong password you choose — do not ship Welcome@123>
FRONTEND_ORIGIN=https://<your-domain>
```

`Host=klarahome-postgres`, `klarahome-redis`, and `klarahome-minio` all resolve via
Docker's embedded DNS once `api` is attached to the `klarahome-data` network (step 4).

The Redis connection string format (`host:port,password=...`) is StackExchange.Redis's
syntax — confirm this matches what `Caching__Redis__ConnectionString` expects in
`Program.cs`/`DependencyInjection.cs` before deploying; adjust if the app parses it
differently (e.g. separate `Password` option rather than inline).

`MINIO_ROOT_USER`/`MINIO_ROOT_PASSWORD` here are this app's **scoped** MinIO credentials
from step 3, not `klarahome-minio`'s actual root account — the variable names are
inherited from the bundled-MinIO case where the api and the container happen to share
one credential pair; under shared infra they're deliberately different keys.

`BOOTSTRAP_ADMIN_EMAIL`/`BOOTSTRAP_ADMIN_PASSWORD` seed the first Super Admin account
on first boot only (see `DbSeeder.SeedBootstrapAdminAsync`) — the seeder never resets
an existing user's password on later runs. Setting an explicit password here means
the account will **not** be forced to change it on first login (only the
random-generated fallback path forces that).

---

## 6. Run the migrator

`SourcingOps.Migrator` (`src/SourcingOps.Migrator/`) is a small standalone console
project — same idea as `KlaraHome.Migrator` — that applies pending EF Core migrations
and exits 0/1. It builds into its own image (`src/SourcingOps.Migrator/Dockerfile`,
no SDK layer, no HTTP surface) and runs via the `migrate` compose profile, so it never
starts alongside a plain `docker compose up` and never races the `api` container.

Pull the full repo checkout onto the VPS (this is the one piece of source-on-VPS
building this deploy still does — the resulting image is small and short-lived, unlike
rebuilding the whole `api` image there):

```bash
cd /opt/sites/<this-app-dir>
git pull   # or clone, on first deploy
```

Then build and run it, attached to `klarahome-data` so it can reach
`klarahome-postgres` directly (no port needs publishing):

```bash
docker compose -f docker-compose.yml -f docker-compose.shared-infra.yml \
  --profile migrate build migrator

docker compose -f docker-compose.yml -f docker-compose.shared-infra.yml \
  --profile migrate run --rm migrator
echo "migrator exit: $?"   # 0 = proceed to step 7, non-zero = stop and investigate
```

It reads `DATABASE_CONNECTION_STRING` from `.env` (set in step 5) the same way `api`
does — no separate credential to manage. Re-running it when there's nothing pending is
a harmless no-op (`GetPendingMigrationsAsync` comes back empty and it exits 0
immediately), so it's safe to run on every deploy rather than only when you know a
migration shipped.

---

## 7. Bring up the `api` container

```bash
cd /opt/sites/<this-app-dir>
docker compose -f docker-compose.yml -f docker-compose.shared-infra.yml up -d --build api
```

Do **not** pass `--profile prod` — that profile only gates the bundled `caddy`
service, which this deploy doesn't use.

Check it started and seeded correctly:

```bash
docker compose logs -f api
```

Look for the seeder's bootstrap admin log line — if `BOOTSTRAP_ADMIN_PASSWORD` was
left blank it prints a one-time generated password here; if you set it explicitly (as
above) you won't see a password in the logs and can log in with what you chose.

Also confirm the api logged a successful Redis connection and did **not** log a
bucket-create attempt for MinIO (it should just find `sourcingops` already there from
step 3 and proceed).

---

## 8. Build the Angular frontend and stage the static files for Caddy

```bash
cd client
npm ci
npm run build
```

Find where the existing Caddy container serves static sites from on the host:

```bash
docker inspect caddy --format '{{json .Mounts}}'
```

Copy the build output into a new subfolder there, e.g.:

```bash
mkdir -p /opt/sites/<caddy-static-root>/sourcingops
cp -r dist/browser/* /opt/sites/<caddy-static-root>/sourcingops/
```

---

## 9. Add a site block to the existing Caddyfile

Find the Caddyfile the running container mounts (from the same `docker inspect caddy`
output above), then append a new site block — do not replace the existing file's
contents, since it's serving other sites too:

```
your-sourcingops-domain.com {
    encode gzip zstd

    handle /api/* {
        reverse_proxy api:8080
    }

    handle {
        root * /srv/www/sourcingops
        try_files {path} /index.html
        file_server
    }
}
```

Adjust `root *` to whatever path the Caddy container sees internally for the folder
you copied files into in step 8 (check the container's volume mount mapping).

`reverse_proxy api:8080` works because the `api` container (named `api` by compose,
in project directory `<this-app-dir>` — check the actual container name with
`docker ps` if compose prefixed it) is now on the same `web` network as `caddy`.

Reload Caddy without downtime:

```bash
docker exec caddy caddy reload --config /etc/caddy/Caddyfile
```

---

## 10. Smoke test

```bash
curl -fsS https://your-sourcingops-domain.com/api/v1/health
```

Then open the site in a browser and log in with `BOOTSTRAP_ADMIN_EMAIL` /
`BOOTSTRAP_ADMIN_PASSWORD` from step 5. Also exercise a feature that touches file
storage (e.g. a catalogue upload / WhatsApp share link) to confirm the scoped MinIO
credentials actually work end-to-end, not just that the bucket exists.

---

## 11. Backups

`deploy/backup.sh` assumes `db`/`minio` services inside this compose project and will
not work as-is (there is no `db` or `minio` container here — they're `klarahome-postgres`
and `klarahome-minio`, owned by a different stack). Back these up as part of whatever
backup routine already covers that stack, e.g.:

```bash
# Postgres
docker exec klarahome-postgres pg_dump -U app sourcingops > sourcingops_$(date +%F).sql

# MinIO — mirror just this app's bucket
docker exec klarahome-minio mc mirror local/sourcingops /backup/sourcingops-minio/
```

Redis here is a cache, not a source of truth (`CACHING_PROVIDER=Redis` only backs the
app's cache layer) — losing it costs a cache warm-up, not data, so it isn't part of this
app's backup routine; whatever already covers `klarahome-redis` for persistence (if any)
is out of scope here.

There is no local `uploads` volume to back up separately under this topology — all file
storage lives in the `sourcingops` MinIO bucket now that `STORAGE_PROVIDER=S3`.
