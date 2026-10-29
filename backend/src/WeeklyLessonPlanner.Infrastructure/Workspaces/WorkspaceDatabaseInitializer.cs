using Microsoft.EntityFrameworkCore;
using Npgsql;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.Infrastructure.Workspaces;

public sealed class WorkspaceDatabaseInitializer(
    WorkspaceDatabaseSettings settings,
    WorkspaceCatalog catalog)
{
    private const long MigrationLockId = 8_642_071_962_026;

    private static readonly string[] PlannerTables =
    [
        "database_metadata",
        "app_config",
        "school_years",
        "courses",
        "course_weekdays",
        "global_day_markers",
        "course_exams",
        "topics",
        "topic_instances",
        "topic_assignments",
        "__EFMigrationsHistory"
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(settings.OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var acquire = new NpgsqlCommand(
            "SELECT pg_advisory_lock($1)", connection);
        acquire.Parameters.AddWithValue(MigrationLockId);
        await acquire.ExecuteNonQueryAsync(cancellationToken);

        try
        {
            await EnsureControlSchemaAsync(connection, cancellationToken);
            await AdoptPublicPlannerAsync(connection, cancellationToken);
            await MigrateReadyWorkspacesAsync(connection, cancellationToken);
            await EnsurePublicContainsNoPlannerObjectsAsync(connection, cancellationToken);
        }
        finally
        {
            await using var release = new NpgsqlCommand(
                "SELECT pg_advisory_unlock($1)", connection);
            release.Parameters.AddWithValue(MigrationLockId);
            await release.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    private static async Task EnsureControlSchemaAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            CREATE SCHEMA IF NOT EXISTS planner_control;

            CREATE TABLE IF NOT EXISTS planner_control.workspaces
            (
                id uuid PRIMARY KEY,
                slug character varying(40) NOT NULL,
                display_name character varying(100) NOT NULL,
                schema_name character varying(63) NOT NULL,
                status smallint NOT NULL,
                created_at timestamp with time zone NOT NULL,
                updated_at timestamp with time zone NOT NULL,
                provisioning_error character varying(1000) NULL,
                CONSTRAINT uq_workspaces_slug UNIQUE (slug),
                CONSTRAINT uq_workspaces_schema_name UNIQUE (schema_name),
                CONSTRAINT ck_workspaces_slug CHECK (
                    slug ~ '^[a-z][a-z0-9-]{1,38}[a-z0-9]$'),
                CONSTRAINT ck_workspaces_status CHECK (status BETWEEN 1 AND 4)
            );

            DO $body$
            BEGIN
                IF NOT EXISTS (
                    SELECT 1
                    FROM pg_constraint
                    WHERE conname = 'ck_workspaces_slug'
                      AND conrelid = 'planner_control.workspaces'::regclass
                ) THEN
                    ALTER TABLE planner_control.workspaces
                    ADD CONSTRAINT ck_workspaces_slug CHECK (
                        slug ~ '^[a-z][a-z0-9-]{1,38}[a-z0-9]$');
                END IF;
            END
            $body$;
            """, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task AdoptPublicPlannerAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var workspaceCount = await ScalarIntAsync(
            connection,
            "SELECT count(*) FROM planner_control.workspaces",
            cancellationToken);
        var hasPublicPlanner = await RelationExistsAsync(
            connection, "public", "app_config", cancellationToken);

        if (!hasPublicPlanner)
        {
            return;
        }

        if (workspaceCount != 0)
        {
            throw new InvalidOperationException(
                "Planner tables still exist in public although the workspace catalog is not empty.");
        }

        var workspaceId = Guid.NewGuid();
        var schemaName = WorkspaceConnections.GenerateSchemaName(workspaceId);
        var quotedSchema = WorkspaceConnections.QuoteIdentifier(schemaName);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var createSchema = new NpgsqlCommand(
            $"CREATE SCHEMA {quotedSchema}", connection, transaction))
        {
            await createSchema.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var table in PlannerTables)
        {
            if (!await RelationExistsAsync(
                    connection, "public", table, cancellationToken, transaction))
            {
                throw new InvalidOperationException(
                    $"Cannot adopt the existing database because public.{table} is missing.");
            }

            var quotedTable = WorkspaceConnections.QuoteIdentifier(table);
            await using var move = new NpgsqlCommand(
                $"ALTER TABLE public.{quotedTable} SET SCHEMA {quotedSchema}",
                connection,
                transaction);
            await move.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var insert = new NpgsqlCommand(
            """
            INSERT INTO planner_control.workspaces
                (id, slug, display_name, schema_name, status, created_at, updated_at)
            VALUES ($1, 'pfrey', 'pfrey', $2, $3, now(), now())
            """, connection, transaction))
        {
            insert.Parameters.AddWithValue(workspaceId);
            insert.Parameters.AddWithValue(schemaName);
            insert.Parameters.AddWithValue((short)WorkspaceStatus.Ready);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private async Task MigrateReadyWorkspacesAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        var workspaces = new List<WorkspaceDescriptor>();
        await using (var command = new NpgsqlCommand(
            """
            SELECT id, slug, display_name, schema_name, status, provisioning_error
            FROM planner_control.workspaces
            WHERE status = $1
            ORDER BY slug
            """, connection))
        {
            command.Parameters.AddWithValue((short)WorkspaceStatus.Ready);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                workspaces.Add(new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    (WorkspaceStatus)reader.GetInt16(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5)));
            }
        }

        foreach (var workspace in workspaces)
        {
            if (!WorkspaceConnections.IsValidSchemaName(workspace.SchemaName))
            {
                throw new InvalidOperationException(
                    $"Workspace {workspace.Slug} has an invalid internal schema name.");
            }

            var connectionString = WorkspaceConnections.ForSchema(
                settings.OwnerConnectionString,
                workspace.SchemaName,
                settings.MaximumPoolSizePerWorkspace);
            var options = new DbContextOptionsBuilder<PlannerDbContext>()
                .UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(typeof(PlannerDbContext).Assembly.FullName))
                .Options;
            await using var dbContext = new PlannerDbContext(options);
            await dbContext.Database.MigrateAsync(cancellationToken);
            await catalog.ApplyRuntimeGrantsAsync(connection, workspace.SchemaName, cancellationToken);
        }
    }

    private static async Task EnsurePublicContainsNoPlannerObjectsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        foreach (var table in PlannerTables)
        {
            if (await RelationExistsAsync(connection, "public", table, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Planner object public.{table} remains after workspace initialization.");
            }
        }
    }

    private static async Task<bool> RelationExistsAsync(
        NpgsqlConnection connection,
        string schema,
        string relation,
        CancellationToken cancellationToken,
        NpgsqlTransaction? transaction = null)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT EXISTS (
                SELECT 1
                FROM pg_catalog.pg_class c
                JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = $1 AND c.relname = $2
                  AND c.relkind IN ('r', 'p')
            )
            """, connection, transaction);
        command.Parameters.AddWithValue(schema);
        command.Parameters.AddWithValue(relation);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task<int> ScalarIntAsync(
        NpgsqlConnection connection,
        string sql,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }
}
