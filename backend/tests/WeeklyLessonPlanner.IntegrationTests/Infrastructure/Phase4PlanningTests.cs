using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Topics;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class Phase4PlanningTests
{
    [PostgresFact]
    public async Task PlaceShiftOverwriteDeleteAndDragPersistAuthoritatively()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var topics = new TopicService(dbContext);
        var planning = new PlanningService(dbContext);
        var course = await CreateCourseAsync(calendar, IsoWeekday.Monday);

        try
        {
            var dates = await FindEligibleDatesAsync(dbContext, IsoWeekday.Monday, 5);
            var instances = new List<Guid>();
            foreach (var heading in new[] { "Alpha", "Bravo", "Charlie", "Delta" })
            {
                var topic = await topics.CreateTopicAsync(new(course.Id, heading, string.Empty));
                instances.Add(Assert.Single(await topics.GetUnplannedInstancesAsync(course.Id, heading)).Id);
            }

            var alpha = await planning.PlaceTopicAsync(new(instances[0], course.Id, dates[0], false));
            var bravo = await planning.PlaceTopicAsync(new(instances[1], course.Id, dates[1], false));
            await planning.PlaceTopicAsync(new(instances[2], course.Id, dates[3], false));
            var inserted = await planning.PlaceTopicAsync(new(instances[3], course.Id, dates[0], true));

            Assert.Equal(dates[0], inserted.InsertedAssignment!.Date);
            Assert.Equal(2, inserted.MovedAssignments.Count);
            Assert.Equal(dates[1], await AssignmentDateAsync(dbContext, alpha.InsertedAssignment!.AssignmentId));
            Assert.Equal(dates[2], await AssignmentDateAsync(dbContext, bravo.InsertedAssignment!.AssignmentId));

            var overwrite = await planning.DragTopicAsync(new(
                inserted.InsertedAssignment.AssignmentId,
                dates[3],
                false,
                false));
            Assert.NotNull(overwrite);
            Assert.Equal(dates[3], overwrite.InsertedAssignment!.Date);
            Assert.Single(overwrite.BecameUnplanned);

            var removed = await planning.RemoveTopicAsync(new(
                alpha.InsertedAssignment.AssignmentId,
                true));
            Assert.NotNull(removed);
            Assert.Contains(removed.BecameUnplanned, item => item.Id == instances[0]);
            Assert.False(await dbContext.TopicAssignments.AnyAsync(
                item => item.Id == alpha.InsertedAssignment.AssignmentId));
        }
        finally
        {
            await DeleteCourseAsync(dbContext, course.Id);
        }
    }

    [PostgresFact]
    public async Task GlobalMarkerShiftsEveryAffectedCourseAndExamShiftsOnlySelectedCourse()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var topics = new TopicService(dbContext);
        var planning = new PlanningService(dbContext);
        var firstCourse = await CreateCourseAsync(calendar, IsoWeekday.Tuesday);
        var secondCourse = await CreateCourseAsync(calendar, IsoWeekday.Tuesday);
        GlobalDayMarkerDto? marker = null;
        CourseExamDto? exam = null;

        try
        {
            var dates = await FindEligibleDatesAsync(dbContext, IsoWeekday.Tuesday, 4, requireGloballyUnassigned: true);
            var first = await CreateAndPlaceAsync(topics, planning, firstCourse.Id, "First A", dates[0]);
            var second = await CreateAndPlaceAsync(topics, planning, secondCourse.Id, "Second A", dates[0]);

            marker = await planning.CreateGlobalMarkerAsync(new(
                dates[0],
                GlobalDayMarkerType.Holiday,
                "Shift both"));
            Assert.Equal(dates[1], await AssignmentDateAsync(dbContext, first.AssignmentId));
            Assert.Equal(dates[1], await AssignmentDateAsync(dbContext, second.AssignmentId));

            exam = await planning.CreateCourseExamAsync(new(firstCourse.Id, dates[1], "First only"));
            Assert.Equal(dates[2], await AssignmentDateAsync(dbContext, first.AssignmentId));
            Assert.Equal(dates[1], await AssignmentDateAsync(dbContext, second.AssignmentId));
        }
        finally
        {
            if (exam is not null)
            {
                await planning.DeleteCourseExamAsync(exam.Id);
            }
            if (marker is not null)
            {
                await planning.DeleteGlobalMarkerAsync(marker.Id);
            }
            await DeleteCourseAsync(dbContext, firstCourse.Id);
            await DeleteCourseAsync(dbContext, secondCourse.Id);
        }
    }

    [PostgresFact]
    public async Task FailedInsertWithNoLaterCapacityRollsBack()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var course = await CreateCourseAsync(calendar, IsoWeekday.Sunday);

        try
        {
            var dates = await FindEligibleDatesAsync(dbContext, IsoWeekday.Sunday, int.MaxValue);
            var assignments = new List<TopicAssignment>();
            for (var index = 0; index <= dates.Count; index++)
            {
                var topic = new Topic
                {
                    Id = Guid.NewGuid(),
                    CourseId = course.Id,
                    Heading = $"Capacity {index:D3}"
                };
                var instance = new TopicInstance
                {
                    Id = Guid.NewGuid(),
                    TopicId = topic.Id,
                    CourseId = course.Id,
                    Topic = topic
                };
                topic.Instances.Add(instance);
                dbContext.Topics.Add(topic);
                if (index < dates.Count)
                {
                    assignments.Add(new TopicAssignment
                    {
                        Id = Guid.NewGuid(),
                        TopicInstanceId = instance.Id,
                        CourseId = course.Id,
                        Date = dates[index],
                        TopicInstance = instance
                    });
                }
            }
            dbContext.TopicAssignments.AddRange(assignments);
            await dbContext.SaveChangesAsync();
            var extra = await dbContext.TopicInstances
                .Where(item => item.CourseId == course.Id && item.Assignment == null)
                .Select(item => item.Id)
                .SingleAsync();
            var original = assignments.ToDictionary(item => item.Id, item => item.Date);

            await Assert.ThrowsAsync<PlanningConflictException>(() =>
                planning.PlaceTopicAsync(new(extra, course.Id, dates[0], true)));

            dbContext.ChangeTracker.Clear();
            var persisted = await dbContext.TopicAssignments
                .Where(item => item.CourseId == course.Id)
                .ToDictionaryAsync(item => item.Id, item => item.Date);
            Assert.Equal(original.Count, persisted.Count);
            Assert.All(original, item => Assert.Equal(item.Value, persisted[item.Key]));
            Assert.False(await dbContext.TopicAssignments.AnyAsync(item => item.TopicInstanceId == extra));
        }
        finally
        {
            await DeleteCourseAsync(dbContext, course.Id);
        }
    }

    private static async Task<AssignmentImpactDto> CreateAndPlaceAsync(
        TopicService topics,
        PlanningService planning,
        Guid courseId,
        string heading,
        DateOnly date)
    {
        await topics.CreateTopicAsync(new(courseId, heading, string.Empty));
        var instance = Assert.Single(await topics.GetUnplannedInstancesAsync(courseId, heading));
        return (await planning.PlaceTopicAsync(new(instance.Id, courseId, date, false))).InsertedAssignment!;
    }

    private static async Task<CourseDto> CreateCourseAsync(CalendarService calendar, IsoWeekday weekday) =>
        await calendar.CreateCourseAsync(new(
            $"Phase 4 {Guid.NewGuid():N}",
            "Integration test",
            [weekday]));

    private static async Task<IReadOnlyList<DateOnly>> FindEligibleDatesAsync(
        PlannerDbContext dbContext,
        IsoWeekday weekday,
        int count,
        bool requireGloballyUnassigned = false)
    {
        var config = await dbContext.AppConfigs.AsNoTracking().SingleAsync();
        var blocked = (await dbContext.GlobalDayMarkers.AsNoTracking().Select(item => item.Date).ToListAsync()).ToHashSet();
        var assigned = requireGloballyUnassigned
            ? (await dbContext.TopicAssignments.AsNoTracking().Select(item => item.Date).ToListAsync()).ToHashSet()
            : [];
        var dates = Enumerable.Range(0, config.PlanningEnd.DayNumber - config.PlanningStart.DayNumber + 1)
            .Select(config.PlanningStart.AddDays)
            .Where(date => ToIsoWeekday(date) == weekday && !blocked.Contains(date) && !assigned.Contains(date))
            .ToArray();
        return count == int.MaxValue ? dates : dates.Take(count).ToArray();
    }

    private static async Task<DateOnly> AssignmentDateAsync(PlannerDbContext dbContext, Guid assignmentId) =>
        await dbContext.TopicAssignments
            .Where(item => item.Id == assignmentId)
            .Select(item => item.Date)
            .SingleAsync();

    private static async Task DeleteCourseAsync(PlannerDbContext dbContext, Guid courseId)
    {
        dbContext.ChangeTracker.Clear();
        var course = await dbContext.Courses.SingleOrDefaultAsync(item => item.Id == courseId);
        if (course is not null)
        {
            dbContext.Courses.Remove(course);
            await dbContext.SaveChangesAsync();
        }
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
