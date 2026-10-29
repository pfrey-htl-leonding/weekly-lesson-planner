using Npgsql;

namespace WeeklyLessonPlanner.Infrastructure.Workspaces;

internal static class WorkspaceConnections
{
    private const string SchemaPrefix = "planner_";

    public static string GenerateSchemaName(Guid workspaceId) =>
        $"{SchemaPrefix}{workspaceId:N}";

    public static bool IsValidSchemaName(string schemaName) =>
        schemaName.Length == SchemaPrefix.Length + 32 &&
        schemaName.StartsWith(SchemaPrefix, StringComparison.Ordinal) &&
        schemaName[SchemaPrefix.Length..].All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string ForSchema(
        string baseConnectionString,
        string schemaName,
        int maximumPoolSize)
    {
        if (!IsValidSchemaName(schemaName))
        {
            throw new InvalidOperationException("The workspace schema name is invalid.");
        }

        var builder = new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            SearchPath = schemaName,
            MinPoolSize = 0,
            MaxPoolSize = maximumPoolSize,
            ConnectionIdleLifetime = 60,
            ConnectionPruningInterval = 10
        };
        return builder.ConnectionString;
    }

    public static string QuoteIdentifier(string identifier) =>
        new NpgsqlCommandBuilder().QuoteIdentifier(identifier);
}
