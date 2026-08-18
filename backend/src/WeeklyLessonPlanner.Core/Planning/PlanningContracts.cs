using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Topics;

namespace WeeklyLessonPlanner.Core.Planning;

public sealed record PlaceTopicCommand(
    Guid TopicInstanceId,
    Guid CourseId,
    DateOnly Date,
    bool InsertShiftsSchedule);

public sealed record RemoveTopicCommand(
    Guid AssignmentId,
    bool DeleteShiftsSchedule);

public sealed record DragTopicCommand(
    Guid AssignmentId,
    DateOnly DestinationDate,
    bool DeleteShiftsSchedule,
    bool InsertShiftsSchedule);

public sealed record MultipleTopicPlanningCommand(
    Guid CourseId,
    DateOnly? From,
    DateOnly? Until);

public sealed record MultipleTopicPlanningResultDto(
    int AffectedTopicCount,
    DateOnly? FirstAffectedDate,
    DateOnly? LastAffectedDate);

public sealed record MoveCourseExamCommand(
    Guid ExamId,
    int Direction);

public sealed record MoveCourseExamResultDto(
    CourseExamDto Exam,
    AssignmentMoveDto? SwappedTopic);

public sealed record CourseRolloverCommand(
    Guid SourceCourseId,
    Guid TargetSchoolYearId,
    DateOnly TargetStartDate,
    IsoWeekday TargetWeekday);

public sealed record CourseRolloverResultDto(
    CourseDto Course,
    int TopicDefinitionCount,
    int TopicInstanceCount,
    int AssignmentCount,
    DateOnly? FirstAssignedDate,
    DateOnly? LastAssignedDate,
    IReadOnlyList<DateOnly> SkippedFixedDates);

public sealed record AssignmentImpactDto(
    Guid AssignmentId,
    Guid TopicInstanceId,
    Guid CourseId,
    DateOnly Date,
    string Heading,
    string Description);

public sealed record AssignmentMoveDto(
    Guid AssignmentId,
    Guid TopicInstanceId,
    DateOnly From,
    DateOnly To);

public sealed record PlanningImpactDto(
    AssignmentImpactDto? InsertedAssignment,
    AssignmentImpactDto? RemovedAssignment,
    IReadOnlyList<AssignmentMoveDto> MovedAssignments,
    IReadOnlyList<DateOnly> AffectedDates,
    IReadOnlyList<TopicInstanceDto> BecameUnplanned);
