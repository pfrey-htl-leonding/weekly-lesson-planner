using System.Globalization;

namespace WeeklyLessonPlanner.Core.Calendar;

public static class CalendarBuilder
{
    public static CalendarViewDto Build(
        AppConfigDto config,
        Guid? courseId,
        IReadOnlySet<IsoWeekday> courseWeekdays,
        IReadOnlyDictionary<DateOnly, GlobalDayMarkerDto> markers,
        IReadOnlyDictionary<DateOnly, CourseExamDto> exams)
    {
        var firstMonday = config.PlanningStart.AddDays(-((int)config.PlanningStart.DayOfWeek + 6) % 7);
        var lastSunday = config.PlanningEnd.AddDays(7 - IsoDay(config.PlanningEnd));
        var weeks = new List<CalendarWeekDto>();

        for (var monday = firstMonday; monday <= lastSunday; monday = monday.AddDays(7))
        {
            var days = config.VisibleWeekdays
                .Order()
                .Select(weekday => BuildDay(
                    monday.AddDays((int)weekday - 1),
                    weekday,
                    config,
                    courseId,
                    courseWeekdays,
                    markers,
                    exams))
                .ToArray();

            weeks.Add(new CalendarWeekDto(
                ISOWeek.GetYear(monday.ToDateTime(TimeOnly.MinValue)),
                ISOWeek.GetWeekOfYear(monday.ToDateTime(TimeOnly.MinValue)),
                days));
        }

        return new CalendarViewDto(
            config.PlanningStart,
            config.PlanningEnd,
            courseId,
            config.VisibleWeekdays,
            weeks);
    }

    private static CalendarDayDto BuildDay(
        DateOnly date,
        IsoWeekday weekday,
        AppConfigDto config,
        Guid? courseId,
        IReadOnlySet<IsoWeekday> courseWeekdays,
        IReadOnlyDictionary<DateOnly, GlobalDayMarkerDto> markers,
        IReadOnlyDictionary<DateOnly, CourseExamDto> exams)
    {
        var inRange = date >= config.PlanningStart && date <= config.PlanningEnd;
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
            label);
    }

    private static int IsoDay(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday
        ? 7
        : (int)date.DayOfWeek;
}
