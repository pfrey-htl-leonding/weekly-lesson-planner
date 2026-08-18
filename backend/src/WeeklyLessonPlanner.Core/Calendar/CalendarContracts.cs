namespace WeeklyLessonPlanner.Core.Calendar;

public enum IsoWeekday
{
    Monday = 1,
    Tuesday = 2,
    Wednesday = 3,
    Thursday = 4,
    Friday = 5,
    Saturday = 6,
    Sunday = 7
}

public enum GlobalDayMarkerType
{
    Holiday = 1,
    Event = 2
}

public enum EffectiveDayState
{
    Normal = 0,
    Holiday = 1,
    Event = 2,
    Exam = 3
}

public sealed record AppConfigDto(
    IReadOnlyList<IsoWeekday> VisibleWeekdays,
    string HolidayColor,
    string EventColor,
    string ExamColor,
    string WeekNumbering = "ISO 8601");

public sealed record UpdateAppConfigCommand(
    IReadOnlyList<IsoWeekday> VisibleWeekdays,
    string HolidayColor,
    string EventColor,
    string ExamColor);

public sealed record SchoolYearDto(
    Guid Id,
    string Name,
    DateOnly PlanningStart,
    DateOnly PlanningEnd);

public sealed record SaveSchoolYearCommand(
    string Name,
    DateOnly PlanningStart,
    DateOnly PlanningEnd);

public sealed record CourseDto(
    Guid Id,
    Guid SchoolYearId,
    string Name,
    string Description,
    IReadOnlyList<IsoWeekday> Weekdays);

public sealed record SaveCourseCommand(
    Guid SchoolYearId,
    string Name,
    string Description,
    IReadOnlyList<IsoWeekday> Weekdays);

public sealed record GlobalDayMarkerDto(
    Guid Id,
    Guid SchoolYearId,
    DateOnly Date,
    GlobalDayMarkerType Type,
    string? Label);

public sealed record SaveGlobalDayMarkerCommand(
    Guid SchoolYearId,
    DateOnly Date,
    GlobalDayMarkerType Type,
    string? Label);

public sealed record SaveGlobalDayMarkerRangeCommand(
    Guid SchoolYearId,
    DateOnly From,
    DateOnly Until,
    GlobalDayMarkerType Type,
    string? Label);

public sealed record CourseExamDto(
    Guid Id,
    Guid CourseId,
    DateOnly Date,
    string Name);

public sealed record SaveCourseExamCommand(Guid CourseId, DateOnly Date, string Name);

public sealed record ScheduledTopicDto(
    Guid AssignmentId,
    Guid TopicInstanceId,
    Guid TopicId,
    Guid CourseId,
    string CourseName,
    string Heading,
    string Description);

public sealed record CalendarDayDto(
    DateOnly Date,
    IsoWeekday Weekday,
    bool IsInPlanningRange,
    bool IsCourseDay,
    EffectiveDayState State,
    string? Label,
    IReadOnlyList<ScheduledTopicDto> ScheduledTopics);

public sealed record CalendarWeekDto(
    int IsoYear,
    int IsoWeek,
    IReadOnlyList<CalendarDayDto> Days);

public sealed record CoursePlanningSummaryDto(
    int LessonDayCount,
    int PlannedTopicCount,
    int UnplannedTopicCount);

public sealed record CalendarViewDto(
    DateOnly PlanningStart,
    DateOnly PlanningEnd,
    Guid SchoolYearId,
    string SchoolYearName,
    Guid? CourseId,
    IReadOnlyList<IsoWeekday> VisibleWeekdays,
    IReadOnlyList<CalendarWeekDto> Weeks,
    CoursePlanningSummaryDto? PlanningSummary = null);
