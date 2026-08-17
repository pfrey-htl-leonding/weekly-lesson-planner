using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Topics;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class Phase52CourseRolloverTests
{
    [PostgresFact]
    public async Task RolloverCopiesTopicsInOrderToDifferentWeekdayAndSkipsTargetMarkers()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var topics = new TopicService(dbContext);
        var planning = new PlanningService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sourceYear = await calendar.CreateSchoolYearAsync(new(
            $"Rollover source {suffix}",
            new DateOnly(2030, 9, 1),
            new DateOnly(2030, 12, 31)));
        var targetYear = await calendar.CreateSchoolYearAsync(new(
            $"Rollover target {suffix}",
            new DateOnly(2031, 9, 1),
            new DateOnly(2031, 12, 31)));

        try
        {
            var sourceCourse = await calendar.CreateCourseAsync(new(
                sourceYear.Id,
                $"Biology {suffix}",
                "Original course description",
                [IsoWeekday.Monday]));
            var sourceMonday = FirstWeekdayOnOrAfter(sourceYear.PlanningStart, IsoWeekday.Monday);

            await topics.CreateTopicAsync(new(sourceCourse.Id, "Alpha", "First topic"));
            var alpha = Assert.Single(await topics.GetUnplannedInstancesAsync(sourceCourse.Id, "Alpha"));
            await planning.PlaceTopicAsync(new(alpha.Id, sourceCourse.Id, sourceMonday, false));
            var repeatedAlpha = await planning.CopyScheduledTopicAsync(alpha.Id);

            await topics.CreateTopicAsync(new(sourceCourse.Id, "Beta", "Second topic"));
            var beta = Assert.Single(await topics.GetUnplannedInstancesAsync(sourceCourse.Id, "Beta"));
            await planning.PlaceTopicAsync(new(beta.Id, sourceCourse.Id, sourceMonday.AddDays(7), false));
            Assert.NotNull(repeatedAlpha);
            await planning.PlaceTopicAsync(new(
                repeatedAlpha.Id,
                sourceCourse.Id,
                sourceMonday.AddDays(14),
                false));
            await topics.CreateTopicAsync(new(sourceCourse.Id, "Gamma", "Remains unplanned"));

            var firstTargetFriday = FirstWeekdayOnOrAfter(targetYear.PlanningStart, IsoWeekday.Friday);
            await planning.CreateGlobalMarkerAsync(new(
                targetYear.Id,
                firstTargetFriday.AddDays(7),
                GlobalDayMarkerType.Holiday,
                "Target holiday"));

            var result = await planning.RollOverCourseAsync(new(
                sourceCourse.Id,
                targetYear.Id,
                targetYear.PlanningStart,
                IsoWeekday.Friday));

            Assert.Equal(sourceCourse.Name, result.Course.Name);
            Assert.Equal(sourceCourse.Description, result.Course.Description);
            Assert.Equal(targetYear.Id, result.Course.SchoolYearId);
            Assert.Equal([IsoWeekday.Friday], result.Course.Weekdays);
            Assert.Equal(3, result.TopicDefinitionCount);
            Assert.Equal(4, result.TopicInstanceCount);
            Assert.Equal(3, result.AssignmentCount);
            Assert.Equal(firstTargetFriday, result.FirstAssignedDate);
            Assert.Equal(firstTargetFriday.AddDays(21), result.LastAssignedDate);
            Assert.Equal([firstTargetFriday.AddDays(7)], result.SkippedFixedDates);

            dbContext.ChangeTracker.Clear();
            var copiedAssignments = await dbContext.TopicAssignments
                .AsNoTracking()
                .Where(item => item.CourseId == result.Course.Id)
                .Include(item => item.TopicInstance)
                .ThenInclude(item => item.Topic)
                .OrderBy(item => item.Date)
                .ToListAsync();
            Assert.Equal(
                [firstTargetFriday, firstTargetFriday.AddDays(14), firstTargetFriday.AddDays(21)],
                copiedAssignments.Select(item => item.Date));
            Assert.Equal(
                ["Alpha", "Beta", "Alpha"],
                copiedAssignments.Select(item => item.TopicInstance.Topic.Heading));
            Assert.Equal(3, await dbContext.TopicAssignments.CountAsync(item => item.CourseId == sourceCourse.Id));
            Assert.False(await dbContext.CourseExams.AnyAsync(item => item.CourseId == result.Course.Id));
            var copiedUnplanned = await topics.GetUnplannedInstancesAsync(result.Course.Id, null);
            Assert.Equal("Gamma", Assert.Single(copiedUnplanned).Heading);
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            await calendar.DeleteSchoolYearAsync(sourceYear.Id);
            await calendar.DeleteSchoolYearAsync(targetYear.Id);
        }
    }

    [PostgresFact]
    public async Task RolloverWithInsufficientCapacityFillsAvailableSlotsAndLeavesOverflowUnplanned()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var topics = new TopicService(dbContext);
        var planning = new PlanningService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sourceYear = await calendar.CreateSchoolYearAsync(new(
            $"Capacity source {suffix}",
            new DateOnly(2032, 9, 1),
            new DateOnly(2032, 10, 31)));
        var targetStart = new DateOnly(2033, 9, 1);
        var onlyTargetMonday = FirstWeekdayOnOrAfter(targetStart, IsoWeekday.Monday);
        var targetYear = await calendar.CreateSchoolYearAsync(new(
            $"Capacity target {suffix}",
            targetStart,
            onlyTargetMonday));

        try
        {
            var sourceCourse = await calendar.CreateCourseAsync(new(
                sourceYear.Id,
                $"Capacity course {suffix}",
                string.Empty,
                [IsoWeekday.Monday]));
            var firstSourceMonday = FirstWeekdayOnOrAfter(sourceYear.PlanningStart, IsoWeekday.Monday);
            foreach (var (heading, offset) in new[] { ("One", 0), ("Two", 7) })
            {
                await topics.CreateTopicAsync(new(sourceCourse.Id, heading, string.Empty));
                var instance = Assert.Single(await topics.GetUnplannedInstancesAsync(sourceCourse.Id, heading));
                await planning.PlaceTopicAsync(new(
                    instance.Id,
                    sourceCourse.Id,
                    firstSourceMonday.AddDays(offset),
                    false));
            }

            var result = await planning.RollOverCourseAsync(new(
                sourceCourse.Id,
                targetYear.Id,
                targetYear.PlanningStart,
                IsoWeekday.Monday));

            dbContext.ChangeTracker.Clear();
            Assert.Equal(1, result.AssignmentCount);
            Assert.Equal(2, result.TopicInstanceCount);
            Assert.Equal(onlyTargetMonday, result.FirstAssignedDate);
            Assert.Equal(onlyTargetMonday, result.LastAssignedDate);
            Assert.Equal(1, await dbContext.TopicAssignments.CountAsync(item => item.CourseId == result.Course.Id));
            var overflow = await topics.GetUnplannedInstancesAsync(result.Course.Id, null);
            Assert.Equal("Two", Assert.Single(overflow).Heading);
            Assert.Equal(2, await dbContext.TopicAssignments.CountAsync(item => item.CourseId == sourceCourse.Id));
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            await calendar.DeleteSchoolYearAsync(sourceYear.Id);
            await calendar.DeleteSchoolYearAsync(targetYear.Id);
        }
    }

    [PostgresFact]
    public async Task RolloverValidationLeavesSourceAndTargetUnchanged()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var sourceYear = await calendar.CreateSchoolYearAsync(new(
            $"Validation source {suffix}",
            new DateOnly(2034, 9, 1),
            new DateOnly(2035, 6, 30)));
        var targetYear = await calendar.CreateSchoolYearAsync(new(
            $"Validation target {suffix}",
            new DateOnly(2035, 9, 1),
            new DateOnly(2036, 6, 30)));

        try
        {
            var courseName = $"Validation course {suffix}";
            var sourceCourse = await calendar.CreateCourseAsync(new(
                sourceYear.Id,
                courseName,
                string.Empty,
                [IsoWeekday.Wednesday]));

            await Assert.ThrowsAsync<ArgumentException>(() => planning.RollOverCourseAsync(new(
                sourceCourse.Id,
                targetYear.Id,
                targetYear.PlanningStart.AddDays(-1),
                IsoWeekday.Thursday)));
            dbContext.ChangeTracker.Clear();
            Assert.False(await dbContext.Courses.AnyAsync(item => item.SchoolYearId == targetYear.Id));

            await calendar.CreateCourseAsync(new(
                targetYear.Id,
                courseName,
                "Existing target",
                [IsoWeekday.Thursday]));
            await Assert.ThrowsAsync<PlanningConflictException>(() => planning.RollOverCourseAsync(new(
                sourceCourse.Id,
                targetYear.Id,
                targetYear.PlanningStart,
                IsoWeekday.Thursday)));

            dbContext.ChangeTracker.Clear();
            Assert.Equal(1, await dbContext.Courses.CountAsync(item => item.SchoolYearId == targetYear.Id));
            Assert.True(await dbContext.Courses.AnyAsync(item => item.Id == sourceCourse.Id));
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            await calendar.DeleteSchoolYearAsync(sourceYear.Id);
            await calendar.DeleteSchoolYearAsync(targetYear.Id);
        }
    }

    private static DateOnly FirstWeekdayOnOrAfter(DateOnly start, IsoWeekday weekday)
    {
        var date = start;
        while (ToIsoWeekday(date) != weekday) date = date.AddDays(1);
        return date;
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
