using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.Infrastructure.Planning;

public sealed class PlanningService(PlannerDbContext dbContext) : IPlanningService
{
    public async Task<PlanningServiceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var provider = dbContext.Database.ProviderName ?? "unknown";
        var available = await dbContext.Database.CanConnectAsync(cancellationToken);

        return new PlanningServiceStatus(nameof(PlanningService), provider, available);
    }
}

