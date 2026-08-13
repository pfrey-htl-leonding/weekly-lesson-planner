using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Core.Topics;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Topics;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class Phase3TopicTests
{
    [PostgresFact]
    public async Task InstancesDeriveVisibilityFromAssignmentsAndSharedDefinition()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var topics = new TopicService(dbContext);
        var planning = new PlanningService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var course = await calendar.CreateCourseAsync(new(
            $"Topic course {suffix}",
            "Phase 3 integration test",
            [IsoWeekday.Wednesday]));
        TopicDto? topic = null;

        try
        {
            topic = await topics.CreateTopicAsync(new(course.Id, "Binary search", "Initial description"));
            Assert.Equal(1, topic.UnplannedInstanceCount);
            await using var reloadedContext = CreateDbContext();
            var reloadedTopics = new TopicService(reloadedContext);
            var source = Assert.Single(await reloadedTopics.GetUnplannedInstancesAsync(course.Id, null));

            dbContext.TopicAssignments.Add(new TopicAssignment
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                TopicInstanceId = source.Id,
                Date = new DateOnly(2027, 3, 3)
            });
            await dbContext.SaveChangesAsync();

            Assert.Empty(await topics.GetUnplannedInstancesAsync(course.Id, null));
            var aggregateView = await calendar.GetCalendarAsync(null);
            var selectedView = await calendar.GetCalendarAsync(course.Id);
            Assert.Equal("Binary search", FindDay(aggregateView, new DateOnly(2027, 3, 3)).ScheduledTopics.Single().Heading);
            Assert.Equal(course.Name, FindDay(selectedView, new DateOnly(2027, 3, 3)).ScheduledTopics.Single().CourseName);
            await Assert.ThrowsAsync<PlanningConflictException>(() =>
                topics.DeleteUnplannedInstanceAsync(source.Id));

            var copy = await planning.CopyScheduledTopicAsync(source.Id);
            Assert.NotNull(copy);
            var unplanned = Assert.Single(await topics.GetUnplannedInstancesAsync(course.Id, "binary"));
            Assert.Equal(copy.Id, unplanned.Id);

            var updated = await topics.UpdateTopicAsync(topic.Id, new(
                course.Id,
                "Binary search trees",
                "Shared updated description"));
            Assert.Equal(2, updated!.TotalInstanceCount);
            Assert.Equal("Binary search trees", Assert.Single(
                await topics.GetUnplannedInstancesAsync(course.Id, "updated")).Heading);

            await Assert.ThrowsAsync<PlanningConflictException>(() => topics.DeleteTopicAsync(topic.Id));
            Assert.True(await topics.DeleteUnplannedInstanceAsync(copy.Id));

            var assignment = await dbContext.TopicAssignments.SingleAsync(item => item.TopicInstanceId == source.Id);
            dbContext.TopicAssignments.Remove(assignment);
            await dbContext.SaveChangesAsync();

            Assert.True(await topics.DeleteTopicAsync(topic.Id));
            topic = null;
            Assert.Empty(await topics.GetTopicsAsync(course.Id));
        }
        finally
        {
            if (topic is not null)
            {
                var assignments = await dbContext.TopicAssignments
                    .Where(item => item.CourseId == course.Id)
                    .ToListAsync();
                dbContext.TopicAssignments.RemoveRange(assignments);
                await dbContext.SaveChangesAsync();
                await topics.DeleteTopicAsync(topic.Id);
            }

            await calendar.DeleteCourseAsync(course.Id);
        }
    }

    [PostgresFact]
    public async Task NewTopicsAreReturnedAlphabeticallyAndStartWithOneInstance()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var topics = new TopicService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var course = await calendar.CreateCourseAsync(new(
            $"Alphabetic course {suffix}",
            string.Empty,
            [IsoWeekday.Monday]));

        try
        {
            await topics.CreateTopicAsync(new(course.Id, "Zebra", string.Empty));
            await topics.CreateTopicAsync(new(course.Id, "alpha", string.Empty));
            await topics.CreateTopicAsync(new(course.Id, "Middle", string.Empty));

            var unplanned = await topics.GetUnplannedInstancesAsync(course.Id, null);
            Assert.Equal(["alpha", "Middle", "Zebra"], unplanned.Select(item => item.Heading));
            Assert.All(await topics.GetTopicsAsync(course.Id), item => Assert.Equal(1, item.TotalInstanceCount));
        }
        finally
        {
            foreach (var topic in await topics.GetTopicsAsync(course.Id))
            {
                await topics.DeleteTopicAsync(topic.Id);
            }
            await calendar.DeleteCourseAsync(course.Id);
        }
    }

    private static PlannerDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")!)
            .Options;
        return new PlannerDbContext(options);
    }

    private static CalendarDayDto FindDay(CalendarViewDto view, DateOnly date) =>
        view.Weeks.SelectMany(week => week.Days).Single(day => day.Date == date);
}
