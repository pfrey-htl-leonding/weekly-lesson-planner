using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace WeeklyLessonPlanner.Infrastructure.Persistence;

public sealed class PlannerDbContextFactory : IDesignTimeDbContextFactory<PlannerDbContext>
{
    private const string ConnectionVariable = "PLANNER_DB_CONNECTION";

    public PlannerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set {ConnectionVariable} before running Entity Framework commands.");
        }

        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PlannerDbContext(options);
    }
}

