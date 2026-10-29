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

The API requires `ConnectionStrings__PlannerDatabase`. For local development,
also enable startup initialization so the workspace catalog and schemas exist:

```bash
export ConnectionStrings__PlannerDatabase='Host=localhost;Port=5432;Database=weekly_lesson_planner;Username=weekly_lesson_planner;Password=...'
export Database__ApplyMigrations=true
dotnet run --project src/WeeklyLessonPlanner.Api
```

Set `PLANNER_DB_CONNECTION` and use `scripts/add-migration.sh <Name>` or `scripts/update-database.sh` for migrations. Do not commit credentials.

Set `TEST_POSTGRES_CONNECTION` to run the provider integration tests; otherwise those tests report as skipped.

## Workspace API

- `GET/POST /api/workspaces`
- `GET /api/workspaces/{workspaceSlug}`
- `POST /api/workspaces/{workspaceSlug}/retry-provisioning`
- `DELETE /api/workspaces/{workspaceSlug}` permanently removes the workspace schema and catalog entry

Every planner endpoint below is scoped under
`/api/workspaces/{workspaceSlug}`. For example, the old `/api/courses` endpoint
is now `/api/workspaces/pfrey/courses` for the `pfrey` workspace.

## Phase 2 API

- `GET/PUT /api/workspaces/{workspaceSlug}/config`
- `GET/POST/PUT/DELETE /api/workspaces/{workspaceSlug}/courses`
- `GET/POST/PUT/DELETE /api/workspaces/{workspaceSlug}/global-markers`
- `GET/POST/PUT/DELETE /api/workspaces/{workspaceSlug}/course-exams`
- `GET /api/workspaces/{workspaceSlug}/calendar?courseId={id}`

Marker and exam writes go through `IPlanningService`. Invalid requests use HTTP 400 Problem Details; exclusivity and destructive-change conflicts use HTTP 409 Problem Details.

## Phase 3 API

- `GET/POST/PUT/DELETE /api/workspaces/{workspaceSlug}/topics`
- `GET /api/workspaces/{workspaceSlug}/topic-instances/unplanned?courseId={id}&search={text}`
- `DELETE /api/workspaces/{workspaceSlug}/topic-instances/{id}` for one unplanned instance
- `POST /api/workspaces/{workspaceSlug}/topic-instances/{scheduledId}/copy`

Creating a definition creates its first unplanned instance. Planned/unplanned status is derived only from assignment presence. Shared definitions cannot be deleted while any instance is scheduled, and scheduled instances cannot be deleted through the unplanned-instance endpoint.

## Schedule import

- `POST /api/workspaces/{workspaceSlug}/planning/import-course-schedule` imports course/date/topic/description rows into one selected course.
- `POST /api/workspaces/{workspaceSlug}/school-years/import` atomically imports a school year and its holiday/event markers.
- `POST /api/workspaces/{workspaceSlug}/courses/import` atomically creates or updates a course catalog.
- `POST /api/workspaces/{workspaceSlug}/topics/import` atomically creates or updates a topic catalog.

Each CSV import is handled by one database transaction, so a validation or
concurrency failure cannot leave a partially imported file. Re-importing the
same file updates matching names/dates and creates only the missing records.

The planned-course import validates the course name, planning range, teaching
weekdays, fixed days, and occupied dates before writing. It reuses matching
topic definitions and unplanned instances, creates additional instances for
repeated topics, and treats already-matching rows as unchanged.
