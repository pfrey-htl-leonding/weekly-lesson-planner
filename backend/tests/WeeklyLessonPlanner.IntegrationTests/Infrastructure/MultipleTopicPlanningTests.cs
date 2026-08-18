using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Topics;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class MultipleTopicPlanningTests
{
    [PostgresFact]
    public async Task AddAndRemoveAllRespectOrderCapacityFixedDaysAndInclusiveInterval()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var topics = new TopicService(dbContext);
        var monday = NextMonday(new DateOnly(2041, 9, 1));
        var schoolYear = await calendar.CreateSchoolYearAsync(new(
            $"Multiple planning test {Guid.NewGuid():N}",
            monday,
            monday.AddDays(6)));

        try
        {
            var course = await calendar.CreateCourseAsync(new(
                schoolYear.Id,
                "Multiple planning course",
                string.Empty,
                [
                    IsoWeekday.Monday,
                    IsoWeekday.Tuesday,
                    IsoWeekday.Wednesday,
                    IsoWeekday.Thursday,
                    IsoWeekday.Friday
                ]));
            await planning.CreateGlobalMarkerAsync(new(
                schoolYear.Id,
                monday.AddDays(1),
                GlobalDayMarkerType.Holiday,
                "Holiday"));
            await planning.CreateCourseExamAsync(new(course.Id, monday.AddDays(2), "Exam"));

            foreach (var heading in new[] { "Charlie", "Existing", "Bravo", "Alpha" })
            {
                await topics.CreateTopicAsync(new(course.Id, heading, string.Empty));
            }
            var existing = (await topics.GetUnplannedInstancesAsync(course.Id, null))
                .Single(item => item.Heading == "Existing");
            dbContext.TopicAssignments.Add(new TopicAssignment
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                TopicInstanceId = existing.Id,
                Date = monday.AddDays(3)
            });
            await dbContext.SaveChangesAsync();

            var added = await planning.AddAllTopicsAsync(new(course.Id, monday, monday.AddDays(4)));

            Assert.Equal(2, added.AffectedTopicCount);
            Assert.Equal(monday, added.FirstAffectedDate);
            Assert.Equal(monday.AddDays(4), added.LastAffectedDate);
            var scheduled = await dbContext.TopicAssignments.AsNoTracking()
                .Where(item => item.CourseId == course.Id)
                .OrderBy(item => item.Date)
                .Select(item => new { item.Date, item.TopicInstance.Topic.Heading })
                .ToListAsync();
            Assert.Equal(
                [(monday, "Alpha"), (monday.AddDays(3), "Existing"), (monday.AddDays(4), "Bravo")],
                scheduled.Select(item => (item.Date, item.Heading)));
            Assert.Equal("Charlie", Assert.Single(await topics.GetUnplannedInstancesAsync(course.Id, null)).Heading);

            var removedFriday = await planning.RemoveAllTopicsAsync(new(
                course.Id,
                monday.AddDays(4),
                monday.AddDays(4)));
            Assert.Equal(1, removedFriday.AffectedTopicCount);
            Assert.Equal(monday.AddDays(4), removedFriday.FirstAffectedDate);

            var removedRemainder = await planning.RemoveAllTopicsAsync(new(course.Id, null, null));
            Assert.Equal(2, removedRemainder.AffectedTopicCount);
            Assert.Empty(await dbContext.TopicAssignments.Where(item => item.CourseId == course.Id).ToListAsync());
            Assert.Equal(
                ["Alpha", "Bravo", "Charlie", "Existing"],
                (await topics.GetUnplannedInstancesAsync(course.Id, null)).Select(item => item.Heading));
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
