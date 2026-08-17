using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Topics;

namespace WeeklyLessonPlanner.Core.Planning;

/// <summary>
/// Defines the application boundary for authoritative planning operations.
/// Keeps schedule mutations transactional without leaking persistence into API endpoints.
/// </summary>
public interface IPlanningService
{
    Task<PlanningServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default);
    Task<PlanningImpactDto> PlaceTopicAsync(
        PlaceTopicCommand command,
        CancellationToken cancellationToken = default);
    Task<PlanningImpactDto?> RemoveTopicAsync(
        RemoveTopicCommand command,
        CancellationToken cancellationToken = default);
    Task<PlanningImpactDto?> DragTopicAsync(
        DragTopicCommand command,
        CancellationToken cancellationToken = default);
    Task<CourseRolloverResultDto> RollOverCourseAsync(
        CourseRolloverCommand command,
        CancellationToken cancellationToken = default);
    Task<GlobalDayMarkerDto> CreateGlobalMarkerAsync(
        SaveGlobalDayMarkerCommand command,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GlobalDayMarkerDto>> CreateGlobalMarkerRangeAsync(
        SaveGlobalDayMarkerRangeCommand command,
        CancellationToken cancellationToken = default);
    Task<GlobalDayMarkerDto?> UpdateGlobalMarkerAsync(
        Guid id,
        SaveGlobalDayMarkerCommand command,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteGlobalMarkerAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CourseExamDto> CreateCourseExamAsync(
        SaveCourseExamCommand command,
        CancellationToken cancellationToken = default);
    Task<CourseExamDto?> UpdateCourseExamAsync(
        Guid id,
        SaveCourseExamCommand command,
        CancellationToken cancellationToken = default);
    Task<bool> DeleteCourseExamAsync(Guid id, CancellationToken cancellationToken = default);
    Task<TopicInstanceDto?> CopyScheduledTopicAsync(
        Guid sourceInstanceId,
        CancellationToken cancellationToken = default);
}
