using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class AggregateExamCalendarTests
{
    [PostgresFact]
    public async Task AllTopicsCalendarIncludesCourseLabelledExams()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var calendar = new CalendarService(dbContext);
        var planning = new PlanningService(dbContext);
        var visibleWeekday = (await calendar.GetConfigAsync()).VisibleWeekdays.First();
        var examDate = NextWeekday(new DateOnly(2045, 9, 1), visibleWeekday);
        var schoolYear = await calendar.CreateSchoolYearAsync(new(
            $"Aggregate exam test {Guid.NewGuid():N}",
            examDate,
            examDate));

        try
        {
            var course = await calendar.CreateCourseAsync(new(
                schoolYear.Id,
                "Algorithms",
                string.Empty,
                [visibleWeekday]));
            var exam = await planning.CreateCourseExamAsync(new(course.Id, examDate, "Written exam"));

            var aggregate = await calendar.GetCalendarAsync(null, schoolYear.Id);

            var day = aggregate.Weeks.SelectMany(week => week.Days).Single(item => item.Date == examDate);
            Assert.Equal(EffectiveDayState.Normal, day.State);
            var scheduledExam = Assert.Single(day.ScheduledExams);
            Assert.Equal(exam.Id, scheduledExam.Id);
            Assert.Equal(course.Id, scheduledExam.CourseId);
            Assert.Equal(course.Name, scheduledExam.CourseName);
            Assert.Equal(exam.Name, scheduledExam.Name);
        }
        finally
        {
            await calendar.DeleteSchoolYearAsync(schoolYear.Id);
        }
    }

    private static DateOnly NextWeekday(DateOnly date, IsoWeekday weekday)
    {
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
