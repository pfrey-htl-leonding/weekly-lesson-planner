using System.Globalization;

namespace WeeklyLessonPlanner.Core.Calendar;

public static class CalendarBuilder
{
    public static CalendarViewDto Build(
        SchoolYearDto schoolYear,
        AppConfigDto config,
        Guid? courseId,
        IReadOnlySet<IsoWeekday> courseWeekdays,
        IReadOnlyDictionary<DateOnly, GlobalDayMarkerDto> markers,
        IReadOnlyDictionary<DateOnly, CourseExamDto> exams,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<ScheduledTopicDto>> scheduledTopics,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<ScheduledExamDto>>? scheduledExams = null)
    {
        var firstMonday = schoolYear.PlanningStart.AddDays(-((int)schoolYear.PlanningStart.DayOfWeek + 6) % 7);
        var lastSunday = schoolYear.PlanningEnd.AddDays(7 - IsoDay(schoolYear.PlanningEnd));
        var weeks = new List<CalendarWeekDto>();
        scheduledExams ??= new Dictionary<DateOnly, IReadOnlyList<ScheduledExamDto>>();

        for (var monday = firstMonday; monday <= lastSunday; monday = monday.AddDays(7))
        {
            var days = config.VisibleWeekdays
                .Order()
                .Select(weekday => BuildDay(
                    monday.AddDays((int)weekday - 1),
                    weekday,
                    schoolYear,
                    config,
                    courseId,
                    courseWeekdays,
                    markers,
                    exams,
                    scheduledTopics,
                    scheduledExams))
                .ToArray();

            weeks.Add(new CalendarWeekDto(
                ISOWeek.GetYear(monday.ToDateTime(TimeOnly.MinValue)),
                ISOWeek.GetWeekOfYear(monday.ToDateTime(TimeOnly.MinValue)),
                days));
        }

        return new CalendarViewDto(
            schoolYear.PlanningStart,
            schoolYear.PlanningEnd,
            schoolYear.Id,
            schoolYear.Name,
            courseId,
            config.VisibleWeekdays,
            weeks);
    }

    private static CalendarDayDto BuildDay(
        DateOnly date,
        IsoWeekday weekday,
        SchoolYearDto schoolYear,
        AppConfigDto config,
        Guid? courseId,
        IReadOnlySet<IsoWeekday> courseWeekdays,
        IReadOnlyDictionary<DateOnly, GlobalDayMarkerDto> markers,
        IReadOnlyDictionary<DateOnly, CourseExamDto> exams,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<ScheduledTopicDto>> scheduledTopics,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<ScheduledExamDto>> scheduledExams)
    {
        var inRange = date >= schoolYear.PlanningStart && date <= schoolYear.PlanningEnd;
        var state = EffectiveDayState.Normal;
        string? label = null;

        if (inRange && markers.TryGetValue(date, out var marker))
        {
            state = marker.Type == GlobalDayMarkerType.Holiday
                ? EffectiveDayState.Holiday
                : EffectiveDayState.Event;
            label = marker.Label;
        }
        else if (inRange && courseId.HasValue && exams.TryGetValue(date, out var exam))
        {
            state = EffectiveDayState.Exam;
            label = exam.Name;
        }

        return new CalendarDayDto(
            date,
            weekday,
            inRange,
            inRange && courseId.HasValue && courseWeekdays.Contains(weekday),
            state,
            label,
            inRange && scheduledTopics.TryGetValue(date, out var topics) ? topics : [],
            inRange && scheduledExams.TryGetValue(date, out var dayExams) ? dayExams : []);
    }

    private static int IsoDay(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday
        ? 7
        : (int)date.DayOfWeek;
}
