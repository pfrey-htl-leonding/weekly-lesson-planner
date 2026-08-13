# Development

## Repository layout

- `frontend/`: Angular 22 application, Material/CDK, API client foundation, and UI wireframe.
- `backend/`: .NET 10 solution, EF Core/Npgsql persistence, migrations, and tests.
- `stack/`: Dockerfiles, Nginx proxy, Docker Compose, and environment template.

See the README in each directory for focused commands. The complete production-style development stack is documented in `stack/README.md`.

## Phase boundary

Phases 0 and 1 provide the executable application foundation only. Calendar, course, topic, marker, and scheduling behavior remain intentionally deferred to their phases in `plan.md`. Confirmed scheduling semantics are preserved as skipped executable specifications under `backend/tests/WeeklyLessonPlanner.UnitTests/Planning`.

