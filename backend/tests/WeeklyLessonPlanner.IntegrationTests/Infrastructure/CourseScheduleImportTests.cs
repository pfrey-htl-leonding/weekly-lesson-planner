using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Topics;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class CourseScheduleImportTests
{
    [PostgresFact]
    public async Task ImportCreatesAndReusesTopicsAndIsIdempotent()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var topics = new TopicService(dbContext);
        var monday = NextMonday(new DateOnly(2042, 9, 1));
        var schoolYear = await calendar.CreateSchoolYearAsync(new(
            $"Schedule import test {Guid.NewGuid():N}",
            monday,
            monday.AddDays(14)));

        try
        {
            var course = await calendar.CreateCourseAsync(new(
                schoolYear.Id,
                "Imported course",
                string.Empty,
                [IsoWeekday.Monday]));
            await topics.CreateTopicAsync(new(course.Id, "Trees", "Existing description"));
            dbContext.ChangeTracker.Clear();
            var command = new ImportCourseScheduleCommand(course.Id,
            [
                new(course.Name, monday, "Trees", "Imported tree description"),
                new(course.Name, monday.AddDays(7), "Trees", "Imported tree description"),
                new(course.Name, monday.AddDays(14), "Graphs", "Imported graph description")
            ]);

            var first = await planning.ImportCourseScheduleAsync(command);
            dbContext.ChangeTracker.Clear();
            var second = await planning.ImportCourseScheduleAsync(command);

            Assert.Equal(3, first.ImportedAssignmentCount);
            Assert.Equal(0, first.UnchangedAssignmentCount);
            Assert.Equal(1, first.CreatedTopicCount);
            Assert.Equal(0, second.ImportedAssignmentCount);
            Assert.Equal(3, second.UnchangedAssignmentCount);
            Assert.Equal(2, await dbContext.Topics.CountAsync(item => item.CourseId == course.Id));
            Assert.Equal(3, await dbContext.TopicInstances.CountAsync(item => item.CourseId == course.Id));
            Assert.Equal(
                [
                    ("Graphs", "Imported graph description"),
                    ("Trees", "Imported tree description")
                ],
                await dbContext.Topics.AsNoTracking()
                    .Where(item => item.CourseId == course.Id)
                    .OrderBy(item => item.Heading)
                    .Select(item => new ValueTuple<string, string>(item.Heading, item.Description))
                    .ToListAsync());
            Assert.Equal(
                [(monday, "Trees"), (monday.AddDays(7), "Trees"), (monday.AddDays(14), "Graphs")],
                await dbContext.TopicAssignments.AsNoTracking()
                    .Where(item => item.CourseId == course.Id)
                    .OrderBy(item => item.Date)
                    .Select(item => new ValueTuple<DateOnly, string>(item.Date, item.TopicInstance.Topic.Heading))
                    .ToListAsync());
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
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
