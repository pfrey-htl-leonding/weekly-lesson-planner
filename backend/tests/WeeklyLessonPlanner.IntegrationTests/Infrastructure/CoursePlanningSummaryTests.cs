using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Topics;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Topics;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class CoursePlanningSummaryTests
{
    [PostgresFact]
    public async Task SummaryCountsEligibleDaysAndTopicInstancesForSelectedCourse()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var topics = new TopicService(dbContext);
        var monday = NextMonday(new DateOnly(2040, 9, 1));
        var schoolYear = await calendar.CreateSchoolYearAsync(new(
            $"Summary test {Guid.NewGuid():N}",
            monday,
            monday.AddDays(6)));

        try
        {
            var course = await calendar.CreateCourseAsync(new(
                schoolYear.Id,
                "Summary course",
                string.Empty,
                [IsoWeekday.Monday, IsoWeekday.Tuesday, IsoWeekday.Wednesday]));
            await planning.CreateGlobalMarkerAsync(new(
                schoolYear.Id,
                monday,
                GlobalDayMarkerType.Holiday,
                "Holiday"));
            await planning.CreateCourseExamAsync(new(course.Id, monday.AddDays(1), "Exam"));

            await topics.CreateTopicAsync(new(course.Id, "Planned", string.Empty));
            await topics.CreateTopicAsync(new(course.Id, "Unplanned", string.Empty));
            var plannedInstance = (await topics.GetUnplannedInstancesAsync(course.Id, null))
                .Single(item => item.Heading == "Planned");
            dbContext.TopicAssignments.Add(new TopicAssignment
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                TopicInstanceId = plannedInstance.Id,
                Date = monday.AddDays(2)
            });
            await dbContext.SaveChangesAsync();

            var view = await calendar.GetCalendarAsync(course.Id, null);

            var summary = Assert.IsType<CoursePlanningSummaryDto>(view.PlanningSummary);
            Assert.Equal(1, summary.LessonDayCount);
            Assert.Equal(1, summary.PlannedTopicCount);
            Assert.Equal(1, summary.UnplannedTopicCount);
        }
        finally
        {
            await calendar.DeleteSchoolYearAsync(schoolYear.Id);
        }
    }

    private static DateOnly NextMonday(DateOnly date) =>
        date.AddDays(((int)DayOfWeek.Monday - (int)date.DayOfWeek + 7) % 7);

    private static PlannerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")!)
            .Options;
        return new PlannerDbContext(options);
    }
}
