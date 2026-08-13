using WeeklyLessonPlanner.Core.Calendar;

namespace WeeklyLessonPlanner.UnitTests.Calendar;

public sealed class CalendarBuilderTests
{
    [Fact]
    public void BuildsInclusiveIsoWeeksAcrossYearBoundary()
    {
        var config = Config(new DateOnly(2020, 12, 31), new DateOnly(2021, 1, 5));

        var view = CalendarBuilder.Build(
            config,
            null,
            new HashSet<IsoWeekday>(),
            new Dictionary<DateOnly, GlobalDayMarkerDto>(),
            new Dictionary<DateOnly, CourseExamDto>());

        Assert.Collection(
            view.Weeks,
            week => { Assert.Equal(2020, week.IsoYear); Assert.Equal(53, week.IsoWeek); },
            week => { Assert.Equal(2021, week.IsoYear); Assert.Equal(1, week.IsoWeek); });
        Assert.False(view.Weeks[0].Days[0].IsInPlanningRange);
        Assert.True(view.Weeks[0].Days[3].IsInPlanningRange);
        Assert.True(view.Weeks[1].Days[1].IsInPlanningRange);
        Assert.False(view.Weeks[1].Days[2].IsInPlanningRange);
    }

    [Fact]
    public void AppliesGlobalMarkersAndOnlySelectedCourseExams()
    {
        var courseId = Guid.NewGuid();
        var holidayDate = new DateOnly(2026, 9, 1);
        var examDate = new DateOnly(2026, 9, 2);
        var config = Config(holidayDate, examDate);
        var markers = new Dictionary<DateOnly, GlobalDayMarkerDto>
        {
            [holidayDate] = new(Guid.NewGuid(), holidayDate, GlobalDayMarkerType.Holiday, "School closed")
        };
        var exams = new Dictionary<DateOnly, CourseExamDto>
        {
            [examDate] = new(Guid.NewGuid(), courseId, examDate, "Written exam")
        };

        var selected = CalendarBuilder.Build(
            config,
            courseId,
            new HashSet<IsoWeekday> { IsoWeekday.Tuesday },
            markers,
            exams);
        var allCourses = CalendarBuilder.Build(
            config,
            null,
            new HashSet<IsoWeekday>(),
            markers,
            new Dictionary<DateOnly, CourseExamDto>());

        Assert.Equal(EffectiveDayState.Holiday, Day(selected, holidayDate).State);
        Assert.Equal(EffectiveDayState.Exam, Day(selected, examDate).State);
        Assert.True(Day(selected, holidayDate).IsCourseDay);
        Assert.Equal(EffectiveDayState.Holiday, Day(allCourses, holidayDate).State);
        Assert.Equal(EffectiveDayState.Normal, Day(allCourses, examDate).State);
    }

    private static AppConfigDto Config(DateOnly start, DateOnly end) => new(
        start,
        end,
        [IsoWeekday.Monday, IsoWeekday.Tuesday, IsoWeekday.Wednesday, IsoWeekday.Thursday, IsoWeekday.Friday],
        "#008000",
        "#0000ff",
        "#ffff00");

    private static CalendarDayDto Day(CalendarViewDto view, DateOnly date) =>
        view.Weeks.SelectMany(week => week.Days).Single(day => day.Date == date);
}
