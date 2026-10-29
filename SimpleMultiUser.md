# Simple multi-workspace plan (PostgreSQL schema variant)

## 1. Status and purpose

This design was implemented for the local stack in October 2026. It remains
the architecture reference for the intentionally unauthenticated first phase.

The goal is to let several people keep independent planner data in one running
Weekly Lesson Planner installation. Each independent planner dataset is called
a **workspace**. Every workspace uses the existing planner data model in its
own PostgreSQL schema.

This version deliberately has **no authentication or authorization**. All
people who can reach the application can list, select, create, read, and change
all workspaces. The workspace selector separates data, but it is not a login
and is not a security boundary.

The implementation must not create a PostgreSQL login role for every person.
Human-facing workspace names and PostgreSQL credentials are separate concepts.

## 2. Goals

- Keep every workspace's planner data separate from every other workspace.
- Reuse the existing `PlannerDbContext`, entities, services, and migrations as
  far as practical.
- Let a user create a new empty workspace from the application.
- Make the active workspace explicit in the browser URL.
- Let users switch workspaces without restarting the stack.
- Preserve the existing planner data by adopting it as the workspace named
  `pfrey`.
- Apply current and future EF Core migrations consistently to every workspace.
- Back up and restore all workspace schemas together.
- Keep database credentials on the server; the frontend must never receive a
  PostgreSQL username, password, connection string, or schema name.
- Put strict limits around workspace provisioning because this version has no
  authentication.

## 3. Non-goals

- Login, logout, passwords, identity providers, or per-user permissions.
- Private workspaces. Every workspace is accessible to every application user.
- Sharing permissions such as owner/editor/viewer.
- Simultaneous collaborative editing beyond the concurrency behavior already
  present in the application.
- A PostgreSQL login role per workspace or person.
- Moving workspaces between PostgreSQL servers.
- Workspace renaming in the first release. In particular, the URL slug should
  initially be immutable.
- Supporting hundreds or thousands of workspaces. The initial design targets a
  small self-hosted installation with at most a few dozen workspaces.

## 4. Terminology

- **Workspace**: one independent planner dataset visible in the UI.
- **Workspace slug**: stable, human-readable identifier used in URLs, for
  example `peter` or `math-department`.
- **Workspace ID**: generated UUID used as the durable application identifier.
- **Workspace schema**: generated PostgreSQL schema containing one complete set
  of planner tables.
- **Control schema**: PostgreSQL schema containing the workspace catalog and its
  own EF Core migration history.
- **Runtime connection**: restricted database connection used for normal
  planner reads and writes.
- **Provisioning connection**: elevated server-side connection used only to
  create schemas, apply migrations, and set grants.

The UI should use the word **workspace**, not `Postgres user`, `database user`,
or `schema`.

## 5. Chosen architecture

Use one PostgreSQL database containing:

```text
weekly_lesson_planner database
|
+-- planner_control schema
|   +-- workspaces
|   +-- __EFMigrationsHistory
|
+-- planner_3f8c... schema
|   +-- app_config
|   +-- school_years
|   +-- courses
|   +-- ...all other planner tables...
|   +-- __EFMigrationsHistory
|
+-- planner_a210... schema
    +-- app_config
    +-- school_years
    +-- courses
    +-- ...all other planner tables...
    +-- __EFMigrationsHistory
```

Each workspace schema contains the same tables currently created in `public`.
The existing planner tables do not receive a `WorkspaceId` column. Selecting a
different schema selects a different dataset.

The control schema is never selected as a planner schema. It has a small,
separate `ControlDbContext` and separate migrations.

### 5.1 Why the schema name is generated

The schema name must not be based on user input. Generate it from the workspace
UUID, for example:

```text
planner_3f8c998741b04e9d8751845136bd096f
```

This provides the following properties:

- It is always a valid unquoted PostgreSQL identifier.
- It is below PostgreSQL's 63-byte identifier limit.
- Workspace display names and slugs can change independently in a later
  release.
- SQL injection through a schema name is structurally avoided.
- Reserved words, spaces, umlauts, and case folding are not database concerns.

The generated name must still be validated against an exact internal pattern
before it is used. Identifiers must be quoted with the Npgsql identifier helper
when they occur in DDL. Values must use query parameters.

### 5.2 Database roles

Use server-side database roles, not roles for human users:

- `planner_owner`: owns `planner_control` and all workspace schemas. It may
  create schemas and run migrations.
- `planner_runtime`: logs in for normal requests. It has `USAGE` on workspace
  schemas and only the DML privileges needed by the application.

The API needs access to both connection strings because workspace creation is
initiated through the API. The normal `PlannerDbContext` must only ever use the
runtime connection. The owner connection must be encapsulated by the migration
and provisioning services and never exposed through ordinary planner services.

Configure default privileges so tables created by later migrations
automatically grant the required table and sequence permissions to
`planner_runtime`. Revoke `CREATE` on `public` from general roles.

This separation limits accidental DDL from normal application code. It does not
protect the database if the API container itself is fully compromised, because
the container necessarily holds the provisioning credential.

## 6. Control schema and workspace catalog

Add a separate `ControlDbContext` whose objects are explicitly mapped to the
`planner_control` schema. Its migration history table must also be explicitly
placed in `planner_control`.

The initial `workspaces` table should contain:

| Column | Type | Rules |
|---|---|---|
| `Id` | UUID | Primary key; generated by the application |
| `Slug` | varchar(40) | Unique; stable; stored lowercase |
| `DisplayName` | varchar(100) | Required; shown to users |
| `SchemaName` | varchar(63) | Unique; generated internally; never returned to the frontend |
| `Status` | smallint | `Provisioning`, `Ready`, `Failed`, or `Disabled` |
| `CreatedAt` | timestamptz | UTC creation time |
| `UpdatedAt` | timestamptz | UTC last status/metadata update |
| `ProvisioningError` | varchar(1000), nullable | Sanitized operator-facing failure summary |
| `xmin` | PostgreSQL xid | Optimistic concurrency token |

Indexes and constraints:

- Unique index on normalized `Slug`.
- Unique index on `SchemaName`.
- Check constraint for the allowed status values.
- Check constraint for the slug format where practical; API validation remains
  the primary source of user-friendly errors.

Do not store connection strings or passwords in this table.

### 6.1 Workspace states

- `Provisioning`: the catalog row exists, but schema creation or migrations are
  still running.
- `Ready`: normal requests may use the workspace.
- `Failed`: provisioning did not complete. Normal planner requests are rejected.
- `Disabled`: the data remains intact, but normal planner requests are rejected.

Only `Ready` workspaces appear in the normal workspace selector. A failed
workspace should appear in the administration section with a concise failure
message and a retry action. Detailed exceptions belong in server logs.

No state should automatically drop a schema.

## 7. Workspace naming and validation

The create form should have two fields:

1. **Workspace name**: required display text; trim leading and trailing
   whitespace; allow ordinary Unicode; maximum 100 characters.
2. **Workspace key**: required URL slug; default it from the display name but
   let the user edit it before creation.

Workspace key rules:

```text
^[a-z][a-z0-9-]{1,38}[a-z0-9]$
```

This means 3-40 lowercase ASCII characters, starting with a letter and ending
with a letter or digit. Consecutive hyphens may either be accepted or collapsed
during automatic generation; this behavior must be covered by tests. Reserve
keys that conflict with application routes, including at least `api`, `health`,
`assets`, `admin`, and `new`.

The slug is a URL identifier, not a PostgreSQL identifier. The generated schema
name remains independent of it.

Both frontend and backend should validate for fast feedback, but backend
validation is authoritative. A duplicate slug returns HTTP 409.

## 8. Selecting the active workspace

### 8.1 Browser route

Use the following frontend route:

```text
/w/{workspaceSlug}
```

Examples:

```text
http://localhost:8080/w/peter
http://localhost:8080/w/math-department
```

The URL is the authoritative active-workspace state. Do not use the PostgreSQL
schema name in the URL.

### 8.2 Remembering the previous selection

Store the last successfully opened slug in local storage as a convenience. Do
not use a cookie as the authoritative selector.

When `/` is opened:

1. Load the list of ready workspaces.
2. If the locally remembered workspace still exists, navigate to it.
3. Otherwise, if exactly one workspace exists, navigate to it.
4. Otherwise, show a workspace chooser.
5. If no workspace exists, show the workspace creation form.

Local storage is only a preference. The backend must derive workspace context
from every API request and never trust a frontend-only global variable.

Using the route keeps separate browser tabs independent. Selecting a workspace
in one tab must not silently switch another tab.

### 8.3 Frontend selector

Place a clearly visible workspace selector in the application header because
the choice affects every tab and every piece of planner state. On selection:

1. Navigate to `/w/{newSlug}`.
2. Cancel or ignore outstanding requests for the old workspace.
3. Clear all course, school-year, calendar, topic, option, and form state.
4. Load the complete state of the new workspace.
5. Update the last-workspace local-storage preference only after the new
   workspace has been confirmed by the API.

The UI must not briefly show the old workspace's data under the new workspace
name.

### 8.4 Workspace creation UI

To stay close to the requested UI, add a **Workspace administration** section
to the `Data import/export` tab, visually separated from CSV controls. It
contains:

- Workspace name.
- Workspace key.
- Create button.
- Provisioning progress.
- A concise failure message and retry button for failed provisioning.
- A delete-workspace button for the active workspace. It must open a
  confirmation dialog that clearly states deletion is final and permanently
  removes all data in that workspace.

The active-workspace selector itself remains in the header. After successful
creation, navigate directly to the new empty workspace.

The create button must be disabled while a request is active and must not issue
two provisioning requests on double-click.

## 9. API shape

### 9.1 Workspace catalog endpoints

Add endpoints outside any workspace context:

```text
GET  /api/workspaces
GET  /api/workspaces/{workspaceSlug}
POST /api/workspaces
POST /api/workspaces/{workspaceSlug}/retry-provisioning
DELETE /api/workspaces/{workspaceSlug}
```

Suggested create request:

```json
{
  "displayName": "Peter",
  "slug": "peter"
}
```

Suggested public response:

```json
{
  "id": "3f8c9987-41b0-4e9d-8751-845136bd096f",
  "displayName": "Peter",
  "slug": "peter",
  "status": "ready"
}
```

Never include `SchemaName`, credentials, connection strings, or raw database
exceptions in an API response.

For the first version, provisioning may run synchronously and return HTTP 201
only after the workspace is ready. The catalog status still records partial
failure safely. If observed provisioning time becomes too long for proxy
timeouts, change creation to return HTTP 202 and poll the workspace resource;
do not keep increasing HTTP timeouts indefinitely.

### 9.2 Workspace-scoped planner endpoints

Move every planner data endpoint under an explicit workspace prefix:

```text
/api/workspaces/{workspaceSlug}/config
/api/workspaces/{workspaceSlug}/school-years
/api/workspaces/{workspaceSlug}/courses
/api/workspaces/{workspaceSlug}/global-markers
/api/workspaces/{workspaceSlug}/course-exams
/api/workspaces/{workspaceSlug}/calendar
/api/workspaces/{workspaceSlug}/topics
/api/workspaces/{workspaceSlug}/topic-instances
/api/workspaces/{workspaceSlug}/planning/...
/api/workspaces/{workspaceSlug}/system/status
```

This is intentionally explicit rather than using a hidden cookie or custom
header. It makes missing scope obvious and makes request-level isolation easier
to test.

Existing unscoped planner endpoints should be removed after the frontend is
updated. They must not silently fall back to a default workspace, because a
missed frontend change could then write to the wrong dataset.

Health endpoints remain unscoped:

```text
/health/live
/health/ready
```

### 9.3 Error behavior

Use Problem Details consistently:

- 400 for invalid workspace names or slugs.
- 404 for an unknown slug.
- 409 for a duplicate slug or a provisioning state conflict.
- 423 or 503 for a known workspace that is not currently usable. Prefer 503 if
  no existing application convention supports 423.
- 500 for an unexpected provisioning failure, while also leaving the catalog
  row in `Failed` state.

## 10. Resolving a workspace for a request

Add request middleware or an endpoint filter that runs before a scoped
`PlannerDbContext` is created:

1. Read `workspaceSlug` from route values.
2. Normalize and validate the slug.
3. Query `ControlDbContext` for the matching workspace.
4. Require `Status == Ready`.
5. Validate `SchemaName` against the internal generated-name pattern.
6. Create an immutable scoped `WorkspaceContext` containing workspace ID, slug,
   display name, and internal schema name.
7. Make the context available to the dynamic `PlannerDbContext` factory and to
   structured logging.

The context must not be mutable after resolution. A request can never switch
workspaces midway through its lifetime.

Catalog endpoints and health endpoints do not resolve a workspace and must not
request `PlannerDbContext` from dependency injection.

## 11. Dynamic `PlannerDbContext`

The current application registers one `PlannerDbContext` with one static
connection string. Replace that registration with a scoped factory that
requires a resolved `WorkspaceContext`.

Build tenant connection strings with `NpgsqlConnectionStringBuilder`:

- Start from the server-side runtime base connection string.
- Set the PostgreSQL search path to exactly the generated workspace schema.
- Do not add `public` as a fallback.
- Apply explicit, configurable connection-pool limits.
- Never concatenate a user-provided slug into the connection string.

The existing EF model uses unqualified table names. PostgreSQL's search path
therefore sends those queries to the selected workspace schema without adding
a workspace field to each entity.

Each distinct search-path connection string creates a distinct Npgsql pool.
For the intended small installation:

- Set minimum pool size to zero.
- Set a small configurable maximum pool size per workspace.
- Configure idle connection lifetime and pruning.
- Set a global configured maximum workspace count.
- Document that raising the workspace limit may require raising PostgreSQL's
  `max_connections` or changing the tenancy design.

Do not implement schema switching by executing a bare session-level
`SET search_path` on arbitrary pooled connections. A leaked session setting
could send a later request to the wrong workspace. The schema selection must be
part of the pool's connection configuration, or otherwise be reset and proven
safe before reuse.

## 12. Workspace provisioning workflow

`IWorkspaceProvisioner` should be the only component allowed to use the owner
connection.

### 12.1 Create workflow

1. Validate and normalize the request.
2. Enforce the configured workspace-count limit.
3. Generate the workspace UUID and schema name.
4. Insert the catalog row with `Provisioning` status. The unique slug constraint
   resolves concurrent create races.
5. Acquire a PostgreSQL advisory lock for that workspace ID.
6. Create the schema with `planner_owner` as owner.
7. Configure schema privileges and default privileges for `planner_runtime`.
8. Build an owner connection whose search path is exactly the new schema.
9. Configure the planner EF migration history table to live in the selected
   schema.
10. Run all `PlannerDbContext` migrations against the new schema.
11. Verify expected baseline objects and seeded rows, including `app_config` and
    the default school year supplied by the current model.
12. Mark the catalog row `Ready`.
13. Release the advisory lock.
14. Return HTTP 201 and the public workspace resource.

If any step after the catalog insert fails:

- Roll back the current database transaction where possible.
- Mark the catalog row `Failed` in a separate reliable operation.
- Store a sanitized error summary and log the complete exception.
- Leave any partially created schema in place for inspection or idempotent
  retry.
- Never automatically issue `DROP SCHEMA ... CASCADE`.

PostgreSQL DDL is largely transactional, but the design must not assume every
future migration operation will be. The state machine and retry behavior remain
necessary.

### 12.2 Retry workflow

Retry is allowed only for a `Failed` workspace:

1. Acquire the same per-workspace advisory lock.
2. Mark the workspace `Provisioning` using optimistic concurrency.
3. Create the schema if it is absent.
4. Reapply grants idempotently.
5. Run pending EF migrations; already recorded migrations are skipped.
6. Verify the schema and mark it `Ready`.

Concurrent retry/create calls must result in one provisioning operation. Other
calls return a conflict without starting a second migration.

## 13. EF Core migration strategy

There are two independent migration streams:

1. `ControlDbContext` migrations for `planner_control`.
2. Existing `PlannerDbContext` migrations, applied once to every workspace
   schema.

Every workspace schema has its own `__EFMigrationsHistory`. This is required so
new workspaces can start from zero and existing workspaces can be upgraded from
their recorded version.

### 13.1 Application startup

When `Database:ApplyMigrations` is enabled:

1. Acquire a global PostgreSQL advisory migration lock so two API instances
   cannot migrate simultaneously.
2. Apply control-schema migrations.
3. Read all `Ready` workspaces.
4. For each workspace, use an owner connection with that workspace's search
   path and apply pending planner migrations.
5. Reapply/verify runtime grants.
6. Release the lock.
7. Only report the application ready after every ready workspace has migrated.

If one workspace migration fails, readiness should fail and startup should log
the affected workspace. Running one application version against a mixture of
old and new workspace schemas is not supported.

This sequential startup process is acceptable for a few dozen small schemas.
If migration time later becomes material, move migrations into a dedicated
one-shot deployment service before scaling the API horizontally.

### 13.2 Creating a new migration

Developer tooling continues to generate planner migrations from
`PlannerDbContext` against a temporary or development workspace schema. Add
scripts/documentation that make the target schema explicit and prevent commands
from accidentally modifying `public`.

Control migrations use a separate design-time factory and output directory.
Migration names and namespaces should make the two streams easy to distinguish.

Every planner migration must be tested against:

- A completely empty workspace.
- A workspace at the immediately previous schema version.
- At least two schemas in the same database.

## 14. Adopting the existing `public` data as `pfrey`

The existing installation has one planner dataset in `public`. Preserve it as
the workspace with display name `pfrey` and slug `pfrey`; do not create an
empty replacement and abandon the data. Its PostgreSQL schema name remains the
generated, UUID-based internal name described in section 5.1.

After adoption, `public` must contain no planner tables, migration history,
sequences, or planner data. It must never be registered as a workspace or used
as a planner search-path fallback.

### 14.1 Required maintenance procedure

1. Stop application writes.
2. Create and verify a full database backup.
3. Create the database roles and the `planner_control` schema.
4. Apply control migrations.
5. Reserve the workspace display name `pfrey` and slug `pfrey`. Stop with a
   clear error if a conflicting catalog entry already exists.
6. Generate its workspace UUID and internal schema name.
7. Create the workspace schema.
8. Move every known planner table from `public` to the workspace schema in one
   transaction:
   - `database_metadata`
   - `app_config`
   - `school_years`
   - `courses`
   - `course_weekdays`
   - `global_day_markers`
   - `course_exams`
   - `topics`
   - `topic_instances`
   - `topic_assignments`
   - `__EFMigrationsHistory`
9. Verify that associated indexes, constraints, and identity-owned sequences
   moved with their tables.
10. Transfer ownership of the moved tables and owned sequences to
    `planner_owner`; moving an object to another schema does not by itself
    normalize ownership.
11. Insert the `Ready` workspace catalog row.
12. Apply and verify owner/runtime grants.
13. Confirm that no planner tables, migration history, owned sequences, or
    planner data remain in `public`.
14. Run migration and isolation smoke tests.
15. Start the new application version.

The adoption operation should be an explicit, versioned, idempotent deployment
step. It should detect and stop on unexpected objects rather than guessing.

### 14.2 Rollback

The previous application expects tables in `public`, so application rollback
also requires either:

- A tested reverse script that moves the exact tables and migration history
  back to `public`, or
- Restoring the pre-adoption database backup.

The release instructions must identify this as a database-layout change and
must not imply that rolling back only the application image is sufficient.

## 15. Frontend work plan

### 15.1 Routing and application boot

- Add routes for `/`, `/w/:workspaceSlug`, and a not-found state.
- Resolve the workspace before issuing any planner API call.
- Centralize construction of the workspace-scoped API base URL.
- Ensure every current API client uses that base; do not hand-build workspace
  prefixes independently in each service.
- Keep the workspace directory API separate from the scoped planner API.

### 15.2 Application state

The current application keeps substantial state in the main component. Add one
explicit reset-and-load path used for both initial navigation and switching.
It must reset at least:

- Selected school year and course.
- Calendar and planning summaries.
- Topics, instances, and assignments.
- Exams and global markers.
- App configuration and form models.
- CSV import/export content where retaining it could cause import into the
  wrong workspace.
- Success and error messages that refer to the old workspace.

Route changes during an outstanding mutation need a defined behavior. The
simplest safe first version is to disable switching while a mutation is in
progress, then navigate after it completes.

### 15.3 Empty and error states

- Zero workspaces: show explanation and creation form.
- Unknown URL slug: show not found and link to the chooser.
- Provisioning workspace: show progress and poll only if asynchronous creation
  is introduced.
- Failed workspace: show the sanitized failure and retry control.
- Backend unavailable: retain the route but do not pretend the workspace is
  empty.

## 16. Backend work plan

Add the following responsibilities without moving planner business rules into
the API layer:

- Workspace catalog contracts and service.
- `ControlDbContext` and migrations.
- Immutable scoped `WorkspaceContext`.
- Route workspace resolver/filter.
- Dynamic planner context factory.
- Workspace provisioner and migrator.
- Endpoint grouping under the workspace prefix.
- Workspace-aware structured logging.
- Health/readiness behavior for control database and migrations.

The existing `CalendarService`, `TopicService`, and `PlanningService` should
continue to accept `PlannerDbContext` and remain unaware of slugs and schema
names. Correct context construction is an infrastructure concern.

## 17. Configuration and Compose changes

Replace the single conceptual database connection with clearly named settings:

```text
ConnectionStrings:ControlDatabase
ConnectionStrings:PlannerRuntimeDatabase
ConnectionStrings:PlannerOwnerDatabase
Workspaces:AllowCreate
Workspaces:MaximumCount
Workspaces:MaximumPoolSizePerWorkspace
Database:ApplyMigrations
```

The concrete environment-variable names follow ASP.NET Core's double-underscore
mapping. Real passwords remain in ignored environment/secrets configuration.
Examples contain placeholders only.

Compose/database initialization must create `planner_owner` and
`planner_runtime` idempotently. The PostgreSQL bootstrap superuser should not be
used by normal API requests.

Keep the PostgreSQL host port bound to `127.0.0.1` by default. Without
authentication, exposing the frontend/API beyond a trusted network also exposes
every workspace.

`Workspaces:AllowCreate=false` disables the create and retry endpoints and hides
their UI. This gives an operator a way to prevent anonymous resource creation
while retaining selection and normal planner use.

`Workspaces:MaximumCount` must be enforced transactionally enough that
concurrent create requests cannot substantially exceed it. It is an operational
safety limit, not a licensing or security mechanism.

On a completely fresh database, startup creates only the control schema. It
does not silently create a planner workspace; the zero-workspace UI guides the
user through creating the first one. The fixed `pfrey` adoption applies only to
the one-time upgrade of an installation that already has planner tables in
`public`.

## 18. Backup and restore

The current shutdown backup dumps the configured database. A database-wide dump
can continue to contain `planner_control` and all workspace schemas, which is a
major advantage of the schema-based design.

Update and test the procedure so that:

- The dump account can read every workspace schema.
- The dump includes the control schema and all per-workspace migration histories.
- New schemas require no backup-script change.
- A restore recreates data before the application starts.
- Role ownership and runtime grants are reapplied after restore, especially
  because the existing dump uses `--no-owner` and `--no-privileges`.
- The restore verification compares catalog entries with actual schemas and
  reports missing or orphaned schemas.
- At least one automated or documented recovery drill restores two workspaces
  and verifies they remain isolated.

Do not implement workspace export by `pg_dump` through the browser as part of
this feature. Existing CSV import/export remains a planner-data feature, not a
database administration tool.

## 19. Health, logging, and diagnostics

### 19.1 Health

- Liveness remains independent of PostgreSQL.
- Readiness verifies the control database, migration completion, and that the
  runtime role can connect.
- Startup migration failure makes the service unready.
- A workspace-specific failure after startup should fail requests for that
  workspace and be visible in logs; define whether it also makes global
  readiness fail. The recommended first version is to fail readiness only for
  schema-version or control-database problems, not for one disabled workspace.

### 19.2 Structured logging

Add these fields when a workspace has been resolved:

- Workspace ID.
- Workspace slug.
- Request path and trace ID through existing ASP.NET Core logging.

Log provisioning state transitions and migration versions. Do not log database
passwords or complete connection strings. Avoid returning internal schema names
to clients, although including them in restricted operator logs is acceptable
when needed for repair.

## 20. Security considerations without authentication

This design prevents accidental mixing of ordinary queries between schemas when
workspace resolution and connection construction are correct. It does **not**
make workspaces private.

Explicitly document the following:

- A workspace slug is public and guessable.
- The workspace list is public to everyone who can reach the API.
- Anyone can modify any workspace.
- If creation is enabled, anyone can consume database resources up to the
  configured maximum.
- The application should remain local-only or on a trusted network.
- PostgreSQL must not be exposed publicly.
- A fixed server-side database secret is acceptable configuration; a shared
  password handed to human users is not part of the design.

Other required protections:

- Never accept a schema name from the frontend.
- Validate generated schema names again at the DDL boundary.
- Quote identifiers and parameterize values.
- Do not include `public` as a search-path fallback.
- Do not permit the runtime role to create tables or schemas.
- Use limits and request throttling around provisioning even on a trusted
  network if the application becomes remotely accessible.

## 21. Test plan

### 21.1 Unit tests

- Slug normalization, validation, reserved names, and length boundaries.
- Internal schema-name generation and validation.
- Workspace-state transitions.
- Connection-string construction preserves the configured server/database and
  selects exactly one generated schema.
- Public workspace DTOs never expose `SchemaName` or secrets.
- Workspace route resolution rejects unknown, failed, provisioning, and
  disabled workspaces.

### 21.2 Backend integration tests with PostgreSQL

- Create a workspace from an empty database and verify every expected table,
  migration history, and seed row exists in its schema.
- Create two workspaces with the same course and school-year names.
- Insert data in workspace A and prove it cannot be read through workspace B's
  endpoints.
- Exercise every endpoint family against a non-default workspace.
- Perform planning mutations in A and prove B remains byte-for-byte/logically
  unchanged.
- Verify transactions and `xmin` concurrency continue to work in each schema.
- Verify a duplicate slug race produces one workspace and one conflict.
- Verify concurrent provisioning/retry uses advisory locking.
- Simulate a failed migration and verify `Failed` state and idempotent retry.
- Verify the runtime role can perform application DML but cannot create or drop
  schemas/tables.
- Verify no planner query succeeds without workspace context.
- Verify old unscoped endpoints do not exist.
- Apply a new migration to two existing schemas and a newly created schema.
- Adopt a populated `public` schema and compare row counts and relationships.

Where practical, use deliberately identical UUIDs in two test schemas to prove
that isolation comes from schema selection rather than globally distinct test
data.

### 21.3 Frontend tests

- Root redirect uses a valid remembered workspace.
- A stale remembered slug falls back to the chooser.
- The URL is the source of truth for the active workspace.
- Switching navigates, clears old state, and reloads all workspace data.
- Separate tabs/routes do not change one another.
- Every planner request includes the workspace API prefix.
- Create validation and duplicate-slug errors are shown correctly.
- Double-clicking Create sends one request.
- Successful creation navigates to the new workspace.
- Switching is disabled during an active mutation.
- Failed workspace provisioning can be retried when creation is enabled.
- The UI never displays schema names or connection details.

### 21.4 Stack and recovery tests

- Fresh Compose startup creates the control schema, presents the zero-workspace
  state, and can create the first workspace through the UI.
- Upgrade of a stack containing existing planner data adopts it without loss.
- Restart does not recreate or duplicate workspaces.
- Shutdown backup includes at least two workspace schemas.
- Restore into an empty database brings back the catalog and both datasets.
- Grants are correct after restore.
- Health checks behave correctly during migrations and after a migration failure.

## 22. Implementation phases

### Phase 0: Confirm decisions

- Approve the terminology and anonymous-access warning.
- Confirm expected maximum workspace count.
- Confirm selector in the header and creation controls in Data import/export.
- Confirm that deletion and renaming are out of scope.
- Confirm synchronous provisioning for the first version.

Exit criterion: all review questions in section 24 have answers.

### Phase 1: Control plane

- Add control-domain contracts and state rules.
- Add `ControlDbContext`, mappings, design-time factory, and migrations.
- Add catalog query/create APIs without provisioning planner schemas yet.
- Add database role/configuration support.

Exit criterion: catalog migrations and workspace metadata tests pass against
PostgreSQL; no existing planner behavior changes.

### Phase 2: Workspace context and isolation

- Add route resolution and immutable scoped workspace context.
- Replace static planner context registration with the dynamic factory.
- Group all planner endpoints under the workspace route.
- Add two-schema isolation integration tests before changing the UI.

Exit criterion: the complete backend test suite passes for two manually prepared
schemas, and no unscoped data endpoint remains.

### Phase 3: Provisioning and migrations

- Implement schema-name generation, DDL, grants, advisory locks, per-schema EF
  migration history, failure state, and retry.
- Replace one-database startup migration with control-plus-all-workspaces
  migration orchestration.
- Add pool limits and workspace-count limits.

Exit criterion: API-created workspaces are ready, empty, isolated, migratable,
and retryable after a simulated failure.

### Phase 4: Existing-data adoption

- Implement and test the explicit public-to-workspace adoption step.
- Write forward, verification, and rollback instructions.
- Run it against a copy of a populated database.

Exit criterion: all rows, relationships, migration records, constraints,
indexes, and identity behavior survive adoption.

### Phase 5: Frontend

- Add routes, chooser, header selector, local-storage preference, scoped API
  base, state reset, and workspace administration controls.
- Add empty, unknown, provisioning, and failed states.
- Update frontend tests.

Exit criterion: a user can create, enter, switch, refresh, and bookmark
workspaces without seeing stale data from another workspace.

### Phase 6: Operations and hardening

- Update Compose configuration and role initialization.
- Update backup, restore, health, logs, README, and stack documentation.
- Run full backend, frontend, stack, adoption, and recovery tests.

Exit criterion: a two-workspace installation can be upgraded, backed up,
restored, and restarted without data mixing or loss.

## 23. Acceptance criteria

The feature is complete only when all of the following are true:

1. Existing planner data appears unchanged in the `pfrey` workspace after
   upgrade, and no planner data or planner objects remain in `public`.
2. A user can create a second empty workspace from the UI.
3. The new workspace has a full migrated planner schema and seeded defaults.
4. The active workspace is visible in `/w/{slug}` and in the header selector.
5. Refreshing or bookmarking preserves the selected workspace.
6. Different tabs can use different workspaces.
7. All planner APIs require an explicit workspace slug.
8. Data created or mutated in one workspace is absent from every other
   workspace.
9. Unknown, failed, or disabled workspaces never fall back to another schema.
10. Normal planner code uses only the restricted runtime connection.
11. Future planner migrations are applied to every ready workspace before the
    new application becomes ready.
12. One database backup and restore recovers the control catalog and all
    workspace schemas.
13. No PostgreSQL credentials or schema names appear in the frontend or public
    API.
14. Documentation clearly states that this version has no authentication and
    that every workspace is accessible to everyone who can reach the app.

## 24. Review decisions required before implementation

1. **Terminology:** Use `workspace` everywhere in the UI, rather than `user`?
2. **Workspace limit:** Is 25 a suitable default maximum for the intended
   installation, or should it be lower/higher?
3. **Creation access:** Should workspace creation default to enabled, or require
   `Workspaces:AllowCreate=true` explicitly?
4. **UI placement:** Approve the selector in the header and creation controls in
   `Data import/export`?
5. **Naming:** Approve separate display name and URL key fields, rather than
   restricting the visible name to PostgreSQL-style ASCII?
6. **Provisioning:** Is synchronous creation acceptable for the first version,
   with a later switch to HTTP 202/polling only if it proves slow?
7. **Lifecycle:** Confirm that rename, disable controls, and deletion UI are not
   part of the first release. (`Disabled` remains an internal/operator state.)
8. **Anonymous behavior:** Confirm that listing and opening every workspace is
   intentional and acceptable because this version contains no authentication.
