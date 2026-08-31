# Docker stack

All container-related files live in this directory. The stack contains PostgreSQL, the .NET API, and the Nginx-served Angular frontend.

## Start

```bash
cp stack/.env.example stack/.env
# Set a private POSTGRES_PASSWORD in stack/.env.
docker compose --env-file stack/.env -f stack/compose.yaml up --build -d
```

Open `http://localhost:8080`. The API is also exposed on loopback at `http://localhost:5080` for development diagnostics.

The API applies committed EF Core migrations before it begins accepting traffic. This startup mode assumes one API replica; use a dedicated migration job before scaling the API horizontally.

## Health and shutdown

```bash
curl http://localhost:5080/health/live
curl http://localhost:5080/health/ready
docker compose --env-file stack/.env -f stack/compose.yaml down
```

Add `--volumes` to `down` only when intentionally deleting local PostgreSQL data.

## PostgreSQL shutdown backups

When the database container is stopped normally, it exports the complete database
schema and contents to a timestamped SQL file before PostgreSQL shuts down. The
files are stored separately from the live database in the
`weekly-lesson-planner_postgres-backups` Docker volume.

List the available exports while the database container is running:

```bash
docker compose --env-file stack/.env -f stack/compose.yaml exec db ls -lh /backups
```

Copy an export to the host by replacing `<backup-file.sql>` with a listed name:

```bash
docker compose --env-file stack/.env -f stack/compose.yaml cp db:/backups/<backup-file.sql> .
```

The hook runs for `stop`, `restart`, and `down` as long as Docker allows the
container to stop gracefully. It cannot run after a host crash, power loss,
`docker kill`, or an out-of-memory termination. Running `down --volumes` also
deletes both the database and backup volumes after the export is created.
