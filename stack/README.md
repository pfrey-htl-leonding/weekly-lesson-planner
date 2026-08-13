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

