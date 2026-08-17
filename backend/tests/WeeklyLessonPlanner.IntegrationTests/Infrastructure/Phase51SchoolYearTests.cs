using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class Phase51SchoolYearTests
{
    [PostgresFact]
    public async Task CoursesRangesAndGlobalMarkersAreScopedBySchoolYear()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var secondYear = await calendar.CreateSchoolYearAsync(new(
            $"2027/28 {suffix}",
            new DateOnly(2027, 9, 1),
            new DateOnly(2028, 6, 30)));
        CourseDto? firstCourse = null;
        CourseDto? secondCourse = null;
        GlobalDayMarkerDto? marker = null;

        try
        {
            var sharedName = $"Repeated name {suffix}";
            firstCourse = await calendar.CreateCourseAsync(new(
                SchoolYear.DefaultId, sharedName, string.Empty, [IsoWeekday.Monday]));
            secondCourse = await calendar.CreateCourseAsync(new(
                secondYear.Id, sharedName, string.Empty, [IsoWeekday.Monday]));
            marker = await planning.CreateGlobalMarkerAsync(new(
                SchoolYear.DefaultId,
                new DateOnly(2026, 9, 7),
                GlobalDayMarkerType.Holiday,
                "Only first year"));

            var secondAggregate = await calendar.GetCalendarAsync(null, secondYear.Id);
            var selectedCourse = await calendar.GetCalendarAsync(secondCourse.Id, SchoolYear.DefaultId);

            Assert.Equal(secondYear.Id, secondAggregate.SchoolYearId);
            Assert.Equal(secondYear.PlanningStart, secondAggregate.PlanningStart);
            Assert.Equal(secondYear.Id, selectedCourse.SchoolYearId);
            Assert.DoesNotContain(secondAggregate.Weeks.SelectMany(week => week.Days),
                day => day.Label == "Only first year");
        }
        finally
        {
            if (marker is not null) await planning.DeleteGlobalMarkerAsync(marker.Id);
            if (firstCourse is not null) await calendar.DeleteCourseAsync(firstCourse.Id);
            if (secondCourse is not null) await calendar.DeleteCourseAsync(secondCourse.Id);
            await calendar.DeleteSchoolYearAsync(secondYear.Id);
        }
    }

    private static PlannerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")!)
            .Options;
        return new PlannerDbContext(options);
    }
}
