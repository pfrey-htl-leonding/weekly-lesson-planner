namespace WeeklyLessonPlanner.Core.Planning;

public sealed class PlanningConflictException : Exception
{
    public PlanningConflictException(string message) : base(message) { }
    public PlanningConflictException(string message, Exception innerException) : base(message, innerException) { }
}
