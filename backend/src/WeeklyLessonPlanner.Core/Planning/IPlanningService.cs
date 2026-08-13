namespace WeeklyLessonPlanner.Core.Planning;

/// <summary>
/// Defines the application boundary for authoritative planning operations.
/// Scheduling commands will be added in Phase 4 without leaking persistence into API endpoints.
/// </summary>
public interface IPlanningService
{
    Task<PlanningServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

