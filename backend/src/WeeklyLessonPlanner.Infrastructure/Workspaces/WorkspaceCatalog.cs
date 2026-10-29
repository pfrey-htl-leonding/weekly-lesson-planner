using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.Infrastructure.Workspaces;

public interface IWorkspaceCatalog
{
    Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default);
    Task<WorkspaceDescriptor?> FindBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<WorkspaceSummary> CreateAsync(CreateWorkspaceCommand command, CancellationToken cancellationToken = default);
    Task<WorkspaceSummary> RetryAsync(string slug, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string slug, CancellationToken cancellationToken = default);
}

public sealed partial class WorkspaceCatalog(
    WorkspaceDatabaseSettings settings,
    ILogger<WorkspaceCatalog> logger) : IWorkspaceCatalog
{
    private const long CatalogWriteLockId = 8_642_071_962_027;

    public async Task<IReadOnlyList<WorkspaceSummary>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(settings.OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, slug, display_name, status, provisioning_error
            FROM planner_control.workspaces
            ORDER BY lower(display_name), slug
            """, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var workspaces = new List<WorkspaceSummary>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var status = (WorkspaceStatus)reader.GetInt16(3);
            workspaces.Add(new(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                StatusName(status),
                reader.IsDBNull(4) ? null : reader.GetString(4)));
        }

        return workspaces;
    }

    public async Task<WorkspaceDescriptor?> FindBySlugAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(settings.OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT id, slug, display_name, schema_name, status, provisioning_error
            FROM planner_control.workspaces
            WHERE slug = $1
            """, connection);
        command.Parameters.AddWithValue(slug.ToLowerInvariant());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new(
            reader.GetGuid(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            (WorkspaceStatus)reader.GetInt16(4),
            reader.IsDBNull(5) ? null : reader.GetString(5));
    }

    public Task<WorkspaceSummary> CreateAsync(
        CreateWorkspaceCommand command,
        CancellationToken cancellationToken = default) =>
        ProvisionAsync(command, null, cancellationToken);

    public async Task<WorkspaceSummary> RetryAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        var workspace = await FindBySlugAsync(slug, cancellationToken)
            ?? throw new KeyNotFoundException("Workspace not found.");
        if (workspace.Status != WorkspaceStatus.Failed)
        {
            throw new WorkspaceConflictException("Only a failed workspace can be retried.");
        }

        return await ProvisionAsync(
            new(workspace.DisplayName, workspace.Slug), workspace, cancellationToken);
    }

    public async Task<bool> DeleteAsync(
        string slug,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new NpgsqlConnection(settings.OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var catalogLock = new NpgsqlCommand(
            "SELECT pg_advisory_xact_lock($1)", connection, transaction))
        {
            catalogLock.Parameters.AddWithValue(CatalogWriteLockId);
            await catalogLock.ExecuteNonQueryAsync(cancellationToken);
        }

        WorkspaceDescriptor? workspace;
        await using (var find = new NpgsqlCommand(
            """
            SELECT id, slug, display_name, schema_name, status, provisioning_error
            FROM planner_control.workspaces
            WHERE slug = $1
            FOR UPDATE
            """, connection, transaction))
        {
            find.Parameters.AddWithValue(slug.Trim().ToLowerInvariant());
            await using var reader = await find.ExecuteReaderAsync(cancellationToken);
            workspace = await reader.ReadAsync(cancellationToken)
                ? new(
                    reader.GetGuid(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    (WorkspaceStatus)reader.GetInt16(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5))
                : null;
        }

        if (workspace is null)
        {
            return false;
        }

        if (workspace.Status == WorkspaceStatus.Provisioning)
        {
            throw new WorkspaceConflictException(
                "A workspace cannot be deleted while it is being provisioned.");
        }

        if (!WorkspaceConnections.IsValidSchemaName(workspace.SchemaName))
        {
            throw new InvalidOperationException(
                $"Workspace {workspace.Slug} has an invalid internal schema name.");
        }

        var quotedSchema = WorkspaceConnections.QuoteIdentifier(workspace.SchemaName);
        await using (var drop = new NpgsqlCommand(
            $"DROP SCHEMA IF EXISTS {quotedSchema} CASCADE", connection, transaction))
        {
            await drop.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var delete = new NpgsqlCommand(
            "DELETE FROM planner_control.workspaces WHERE id = $1", connection, transaction))
        {
            delete.Parameters.AddWithValue(workspace.Id);
            await delete.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        logger.LogWarning(
            "Permanently deleted workspace {WorkspaceId} ({WorkspaceSlug}) and schema {SchemaName}",
            workspace.Id,
            workspace.Slug,
            workspace.SchemaName);
        return true;
    }

    private async Task<WorkspaceSummary> ProvisionAsync(
        CreateWorkspaceCommand request,
        WorkspaceDescriptor? existing,
        CancellationToken cancellationToken)
    {
        if (!settings.AllowCreate)
        {
            throw new WorkspaceConflictException("Workspace creation is disabled.");
        }

        var displayName = request.DisplayName.Trim();
        var slug = request.Slug.Trim().ToLowerInvariant();
        Validate(displayName, slug);

        var workspaceId = existing?.Id ?? Guid.NewGuid();
        var schemaName = existing?.SchemaName ?? WorkspaceConnections.GenerateSchemaName(workspaceId);

        await using var connection = new NpgsqlConnection(settings.OwnerConnectionString);
        await connection.OpenAsync(cancellationToken);

        if (existing is null)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await using (var catalogLock = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock($1)", connection, transaction))
            {
                catalogLock.Parameters.AddWithValue(CatalogWriteLockId);
                await catalogLock.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var countCommand = new NpgsqlCommand(
                "SELECT count(*) FROM planner_control.workspaces", connection, transaction))
            {
                var count = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
                if (count >= settings.MaximumCount)
                {
                    throw new WorkspaceConflictException(
                        $"The configured limit of {settings.MaximumCount} workspaces has been reached.");
                }
            }

            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO planner_control.workspaces
                    (id, slug, display_name, schema_name, status, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $5, now(), now())
                """, connection, transaction);
            insert.Parameters.AddWithValue(workspaceId);
            insert.Parameters.AddWithValue(slug);
            insert.Parameters.AddWithValue(displayName);
            insert.Parameters.AddWithValue(schemaName);
            insert.Parameters.AddWithValue((short)WorkspaceStatus.Provisioning);
            try
            {
                await insert.ExecuteNonQueryAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new WorkspaceConflictException($"A workspace with key “{slug}” already exists.");
            }
        }
        else
        {
            await using var update = new NpgsqlCommand(
                """
                UPDATE planner_control.workspaces
                SET status = $2, provisioning_error = NULL, updated_at = now()
                WHERE id = $1 AND status = $3
                """, connection);
            update.Parameters.AddWithValue(workspaceId);
            update.Parameters.AddWithValue((short)WorkspaceStatus.Provisioning);
            update.Parameters.AddWithValue((short)WorkspaceStatus.Failed);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1)
            {
                throw new WorkspaceConflictException("The workspace state changed before retry started.");
            }
        }

        try
        {
            await ProvisionSchemaAsync(connection, schemaName, cancellationToken);
            await SetStateAsync(connection, workspaceId, WorkspaceStatus.Ready, null, cancellationToken);
            return new(workspaceId, slug, displayName, StatusName(WorkspaceStatus.Ready), null);
        }
        catch (Exception exception)
        {
            logger.LogError(exception,
                "Provisioning workspace {WorkspaceId} ({WorkspaceSlug}) failed",
                workspaceId,
                slug);
            const string summary =
                "Workspace provisioning failed. Review the API logs, then retry.";
            await SetStateAsync(connection, workspaceId, WorkspaceStatus.Failed, summary, cancellationToken);
            throw;
        }
    }

    private async Task ProvisionSchemaAsync(
        NpgsqlConnection connection,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var quotedSchema = WorkspaceConnections.QuoteIdentifier(schemaName);
        await using (var create = new NpgsqlCommand(
            $"CREATE SCHEMA IF NOT EXISTS {quotedSchema}", connection))
        {
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var connectionString = WorkspaceConnections.ForSchema(
            settings.OwnerConnectionString,
            schemaName,
            settings.MaximumPoolSizePerWorkspace);
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(PlannerDbContext).Assembly.FullName))
            .Options;
        await using var dbContext = new PlannerDbContext(options);
        await dbContext.Database.MigrateAsync(cancellationToken);

        await ApplyRuntimeGrantsAsync(connection, schemaName, cancellationToken);
    }

    internal async Task ApplyRuntimeGrantsAsync(
        NpgsqlConnection connection,
        string schemaName,
        CancellationToken cancellationToken)
    {
        var runtimeUser = new NpgsqlConnectionStringBuilder(settings.RuntimeConnectionString).Username;
        var ownerUser = new NpgsqlConnectionStringBuilder(settings.OwnerConnectionString).Username;
        if (string.IsNullOrWhiteSpace(runtimeUser) || runtimeUser == ownerUser)
        {
            return;
        }

        var schema = WorkspaceConnections.QuoteIdentifier(schemaName);
        var role = WorkspaceConnections.QuoteIdentifier(runtimeUser);
        var sql = $"""
            GRANT USAGE ON SCHEMA {schema} TO {role};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {schema} TO {role};
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA {schema} TO {role};
            ALTER DEFAULT PRIVILEGES IN SCHEMA {schema}
                GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO {role};
            ALTER DEFAULT PRIVILEGES IN SCHEMA {schema}
                GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO {role};
            """;
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetStateAsync(
        NpgsqlConnection connection,
        Guid workspaceId,
        WorkspaceStatus status,
        string? error,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            UPDATE planner_control.workspaces
            SET status = $2, provisioning_error = $3, updated_at = now()
            WHERE id = $1
            """, connection);
        command.Parameters.AddWithValue(workspaceId);
        command.Parameters.AddWithValue((short)status);
        command.Parameters.AddWithValue((object?)error ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Validate(string displayName, string slug)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > 100)
        {
            throw new WorkspaceValidationException(
                "Workspace name must contain between 1 and 100 characters.");
        }

        if (!SlugPattern().IsMatch(slug))
        {
            throw new WorkspaceValidationException(
                "Workspace key must contain 3–40 lowercase letters, digits, or hyphens; " +
                "it must start with a letter and end with a letter or digit.");
        }

        if (ReservedSlugs.Contains(slug))
        {
            throw new WorkspaceValidationException($"The workspace key “{slug}” is reserved.");
        }
    }

    private static string StatusName(WorkspaceStatus status) =>
        status.ToString().ToLowerInvariant();

    private static readonly HashSet<string> ReservedSlugs =
        ["api", "health", "assets", "admin", "new"];

    [GeneratedRegex("^[a-z][a-z0-9-]{1,38}[a-z0-9]$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}
