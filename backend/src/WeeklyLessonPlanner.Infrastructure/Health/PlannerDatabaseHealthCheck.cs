using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using WeeklyLessonPlanner.Infrastructure.Workspaces;

namespace WeeklyLessonPlanner.Infrastructure.Health;

internal sealed class PlannerDatabaseHealthCheck(WorkspaceDatabaseSettings settings) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(settings.RuntimeConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM planner_control.workspaces)", connection);
            await command.ExecuteScalarAsync(cancellationToken);
            return HealthCheckResult.Healthy("PostgreSQL and the workspace catalog are reachable.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL readiness check failed.", exception);
        }
    }
}
