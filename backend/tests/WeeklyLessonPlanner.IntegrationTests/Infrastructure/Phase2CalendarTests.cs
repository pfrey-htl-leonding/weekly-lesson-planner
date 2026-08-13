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
        var date = new DateOnly(2026, 10, 12);
        var otherDate = date.AddDays(1);
        var course = await calendar.CreateCourseAsync(new(
            $"Phase 2 {suffix}",
            "Integration test",
            [IsoWeekday.Monday, IsoWeekday.Tuesday]));

        try
        {
            var updatedCourse = await calendar.UpdateCourseAsync(course.Id, new(
                course.Name,
                "Updated integration test",
                [IsoWeekday.Tuesday]));
            Assert.Equal([IsoWeekday.Tuesday], updatedCourse!.Weekdays);

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
        }
        finally
        {
            await calendar.DeleteCourseAsync(course.Id);
        }
    }

    private static CalendarDayDto FindDay(CalendarViewDto view, DateOnly date) =>
        view.Weeks.SelectMany(week => week.Days).Single(day => day.Date == date);

    private static PlannerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")!)
            .Options;
        return new PlannerDbContext(options);
    }
}
