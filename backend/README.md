# Weekly Lesson Planner backend

.NET 10 Minimal Web API with EF Core 10 and Npgsql.

## Projects

- `WeeklyLessonPlanner.Api`: HTTP, OpenAPI, Problem Details, CORS, and health endpoints.
- `WeeklyLessonPlanner.Core`: application contracts and future domain rules.
- `WeeklyLessonPlanner.Infrastructure`: PostgreSQL context, migrations, health check, and planning-service implementation.
- `WeeklyLessonPlanner.UnitTests`: fast tests and the pending Phase 4 planning specifications.
- `WeeklyLessonPlanner.IntegrationTests`: DI and real-PostgreSQL provider probes.

## Commands

```bash
cd backend
dotnet tool restore
dotnet restore WeeklyLessonPlanner.sln --locked-mode
dotnet build WeeklyLessonPlanner.sln --no-restore
dotnet test WeeklyLessonPlanner.sln --no-build
```

The API requires `ConnectionStrings__PlannerDatabase`. For local development:

```bash
export ConnectionStrings__PlannerDatabase='Host=localhost;Port=5432;Database=weekly_lesson_planner;Username=weekly_lesson_planner;Password=...'
dotnet run --project src/WeeklyLessonPlanner.Api
```

Set `PLANNER_DB_CONNECTION` and use `scripts/add-migration.sh <Name>` or `scripts/update-database.sh` for migrations. Do not commit credentials.

Set `TEST_POSTGRES_CONNECTION` to run the provider integration tests; otherwise those tests report as skipped.

## Phase 2 API

- `GET/PUT /api/config`
- `GET/POST/PUT/DELETE /api/courses`
- `GET/POST/PUT/DELETE /api/global-markers`
- `GET/POST/PUT/DELETE /api/course-exams`
- `GET /api/calendar?courseId={id}`

Marker and exam writes go through `IPlanningService`. Invalid requests use HTTP 400 Problem Details; exclusivity and destructive-change conflicts use HTTP 409 Problem Details.
