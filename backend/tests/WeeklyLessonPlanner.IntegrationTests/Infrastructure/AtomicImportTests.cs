using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Topics;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Topics;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class AtomicImportTests
{
    [PostgresFact]
    public async Task CatalogImportsValidateWholeBatchAndPersistCompleteBatch()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var topics = new TopicService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var start = new DateOnly(2045, 9, 1);

        var schoolYear = await planning.ImportSchoolYearAsync(new(
            $"Atomic import {suffix}",
            start,
            start.AddDays(30),
            [
                new(start.AddDays(2), GlobalDayMarkerType.Holiday, "Holiday"),
                new(start.AddDays(3), GlobalDayMarkerType.Event, "Event")
            ]));

        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() =>
                calendar.ImportCourseCatalogAsync(new(
                    schoolYear.Id,
                    [IsoWeekday.Monday],
                    [
                        new("Must not be created", string.Empty),
                        new("Invalid", new string('x', 2001))
                    ])));
            Assert.Empty(await calendar.GetCoursesAsync(schoolYear.Id));

            var courseImport = await calendar.ImportCourseCatalogAsync(new(
                schoolYear.Id,
                [IsoWeekday.Monday],
                [new("Algorithms", "Trees"), new("Databases", "SQL")]));
            var courses = await calendar.GetCoursesAsync(schoolYear.Id);

            Assert.Equal(new ImportCourseCatalogResultDto(2, 0, 2), courseImport);
            Assert.Equal(2, courses.Count);
            Assert.Equal(2, (await calendar.GetGlobalMarkersAsync(schoolYear.Id)).Count);

            var course = courses.Single(item => item.Name == "Algorithms");
            await Assert.ThrowsAsync<ArgumentException>(() =>
                topics.ImportTopicCatalogAsync(new(
                    course.Id,
                    [
                        new("Must not be created", string.Empty),
                        new("Invalid", new string('x', 4001))
                    ])));
            Assert.Empty(await topics.GetTopicsAsync(course.Id));

            var topicImport = await topics.ImportTopicCatalogAsync(new(
                course.Id,
                [new("Trees", "Balanced trees"), new("Graphs", "Shortest paths")]));

            Assert.Equal(new ImportTopicCatalogResultDto(2, 0, 2), topicImport);
            Assert.Equal(2, (await topics.GetTopicsAsync(course.Id)).Count);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            await calendar.DeleteSchoolYearAsync(schoolYear.Id);
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
