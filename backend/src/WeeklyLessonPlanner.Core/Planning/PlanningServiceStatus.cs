namespace WeeklyLessonPlanner.Core.Planning;

public sealed record PlanningServiceStatus(
    string Service,
    string DatabaseProvider,
    bool DatabaseAvailable);

