using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Topics;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class MoveCourseExamTests
{
    [PostgresFact]
    public async Task MoveSwapsDestinationTopicAndSkipsOtherFixedDays()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var topics = new TopicService(dbContext);
        var firstMonday = NextMonday(new DateOnly(2042, 9, 1));
        var schoolYear = await calendar.CreateSchoolYearAsync(new(
            $"Exam movement test {Guid.NewGuid():N}",
            firstMonday,
            firstMonday.AddDays(28)));

        try
        {
            var course = await calendar.CreateCourseAsync(new(
                schoolYear.Id,
                "Exam movement course",
                string.Empty,
                [IsoWeekday.Monday]));
            await topics.CreateTopicAsync(new(course.Id, "Destination topic", string.Empty));
            var topic = Assert.Single(await topics.GetUnplannedInstancesAsync(course.Id, null));
            dbContext.TopicAssignments.Add(new TopicAssignment
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                TopicInstanceId = topic.Id,
                Date = firstMonday.AddDays(7)
            });
            await dbContext.SaveChangesAsync();
            var exam = await planning.CreateCourseExamAsync(new(course.Id, firstMonday, "Written exam"));
            await planning.CreateGlobalMarkerAsync(new(
                schoolYear.Id,
                firstMonday.AddDays(14),
                GlobalDayMarkerType.Holiday,
                "Holiday"));
            await planning.CreateCourseExamAsync(new(course.Id, firstMonday.AddDays(21), "Other exam"));

            var swapped = await planning.MoveCourseExamAsync(new(exam.Id, 1));

            Assert.Equal(firstMonday.AddDays(7), swapped.Exam.Date);
            Assert.NotNull(swapped.SwappedTopic);
            Assert.Equal(firstMonday.AddDays(7), swapped.SwappedTopic.From);
            Assert.Equal(firstMonday, swapped.SwappedTopic.To);
            Assert.Equal(firstMonday, await AssignmentDateAsync(dbContext, topic.Id));

            var skipped = await planning.MoveCourseExamAsync(new(exam.Id, 1));

            Assert.Equal(firstMonday.AddDays(28), skipped.Exam.Date);
            Assert.Null(skipped.SwappedTopic);
            await Assert.ThrowsAsync<PlanningConflictException>(() =>
                planning.MoveCourseExamAsync(new(exam.Id, 1)));
        }
        finally
        {
            await calendar.DeleteSchoolYearAsync(schoolYear.Id);
        }
    }

    private static Task<DateOnly> AssignmentDateAsync(PlannerDbContext dbContext, Guid topicInstanceId) =>
        dbContext.TopicAssignments.AsNoTracking()
            .Where(item => item.TopicInstanceId == topicInstanceId)
            .Select(item => item.Date)
            .SingleAsync();

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
