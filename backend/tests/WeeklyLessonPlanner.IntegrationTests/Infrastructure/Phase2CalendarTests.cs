using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class Phase2CalendarTests
{
    [PostgresFact]
    public async Task MarkerAndExamAreExclusiveAndCourseScoped()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var date = await FindFreeBlockAsync(dbContext, 2);
        var otherDate = date.AddDays(1);
        var firstWeekday = ToIsoWeekday(date);
        var secondWeekday = ToIsoWeekday(otherDate);
        var course = await calendar.CreateCourseAsync(new(
            $"Phase 2 {suffix}",
            "Integration test",
            [firstWeekday, secondWeekday]));

        try
        {
            var updatedCourse = await calendar.UpdateCourseAsync(course.Id, new(
                course.Name,
                "Updated integration test",
                [secondWeekday]));
            Assert.Equal([secondWeekday], updatedCourse!.Weekdays);

            var marker = await planning.CreateGlobalMarkerAsync(new(
                date,
                GlobalDayMarkerType.Holiday,
                "Holiday"));

            await Assert.ThrowsAsync<PlanningConflictException>(() => planning.CreateCourseExamAsync(new(
                course.Id,
                date,
                "Conflicting exam")));

            var exam = await planning.CreateCourseExamAsync(new(course.Id, otherDate, "Course exam"));
            var view = await calendar.GetCalendarAsync(course.Id);
            var withoutCourse = await calendar.GetCalendarAsync(null);

            Assert.Equal(EffectiveDayState.Holiday, FindDay(view, date).State);
            Assert.Equal(EffectiveDayState.Exam, FindDay(view, otherDate).State);
            Assert.True(FindDay(view, otherDate).IsCourseDay);
            Assert.Equal(EffectiveDayState.Normal, FindDay(withoutCourse, otherDate).State);

            await planning.DeleteCourseExamAsync(exam.Id);
            await planning.DeleteGlobalMarkerAsync(marker.Id);

            var rangeStart = await FindFreeBlockAsync(dbContext, 3, date.AddDays(7));
            var rangeEnd = rangeStart.AddDays(2);
            var blockingExam = await planning.CreateCourseExamAsync(new(
                course.Id,
                rangeStart.AddDays(1),
                "Range conflict"));
            await Assert.ThrowsAsync<PlanningConflictException>(() =>
                planning.CreateGlobalMarkerRangeAsync(new(
                    rangeStart,
                    rangeEnd,
                    GlobalDayMarkerType.Holiday,
                    "Autumn break")));
            Assert.False(await dbContext.GlobalDayMarkers.AnyAsync(
                item => item.Date >= rangeStart && item.Date <= rangeEnd));

            await planning.DeleteCourseExamAsync(blockingExam.Id);
            var range = await planning.CreateGlobalMarkerRangeAsync(new(
                rangeStart,
                rangeEnd,
                GlobalDayMarkerType.Holiday,
                "Autumn break"));
            Assert.Equal(3, range.Count);
            Assert.Equal(
                [rangeStart, rangeStart.AddDays(1), rangeEnd],
                range.Select(item => item.Date));
            foreach (var rangeMarker in range)
            {
                await planning.DeleteGlobalMarkerAsync(rangeMarker.Id);
            }
        }
        finally
        {
            await calendar.DeleteCourseAsync(course.Id);
        }
    }

    private static CalendarDayDto FindDay(CalendarViewDto view, DateOnly date) =>
        view.Weeks.SelectMany(week => week.Days).Single(day => day.Date == date);

    private static async Task<DateOnly> FindFreeBlockAsync(
        PlannerDbContext dbContext,
        int length,
        DateOnly? notBefore = null)
    {
        var config = await dbContext.AppConfigs.AsNoTracking().SingleAsync();
        var markers = (await dbContext.GlobalDayMarkers.AsNoTracking().Select(item => item.Date).ToListAsync()).ToHashSet();
        var exams = (await dbContext.CourseExams.AsNoTracking().Select(item => item.Date).ToListAsync()).ToHashSet();
        var assignments = (await dbContext.TopicAssignments.AsNoTracking().Select(item => item.Date).ToListAsync()).ToHashSet();
        var first = notBefore is { } requested && requested > config.PlanningStart ? requested : config.PlanningStart;
        for (var date = first; date.AddDays(length - 1) <= config.PlanningEnd; date = date.AddDays(1))
        {
            if (Enumerable.Range(0, length).Select(date.AddDays)
                .All(candidate => !markers.Contains(candidate) && !exams.Contains(candidate) && !assignments.Contains(candidate)))
            {
                return date;
            }
        }

        throw new InvalidOperationException("No free calendar block is available for the integration test.");
    }

    private static IsoWeekday ToIsoWeekday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday
        ? IsoWeekday.Sunday
        : (IsoWeekday)date.DayOfWeek;

    private static PlannerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")!)
            .Options;
        return new PlannerDbContext(options);
    }
}
