using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.Infrastructure.Calendar;

public sealed class CalendarService(PlannerDbContext dbContext) : ICalendarService
{
    public async Task<AppConfigDto> GetConfigAsync(CancellationToken cancellationToken = default) =>
        ToDto(await GetConfigEntityAsync(cancellationToken));

    public async Task<AppConfigDto> UpdateConfigAsync(
        UpdateAppConfigCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateConfig(command);
        var config = await GetConfigEntityAsync(cancellationToken);

        var assignmentsOutsideRange = await dbContext.TopicAssignments.AnyAsync(
            item => item.Date < command.PlanningStart || item.Date > command.PlanningEnd,
            cancellationToken);
        if (assignmentsOutsideRange)
        {
            throw new PlanningConflictException(
                "The planning range cannot exclude scheduled topics. Remove or move them first.");
        }

        config.PlanningStart = command.PlanningStart;
        config.PlanningEnd = command.PlanningEnd;
        config.VisibleWeekdaysMask = ToMask(command.VisibleWeekdays);
        config.HolidayColor = command.HolidayColor.Trim();
        config.EventColor = command.EventColor.Trim();
        config.ExamColor = command.ExamColor.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(config);
    }

    public async Task<IReadOnlyList<CourseDto>> GetCoursesAsync(
        CancellationToken cancellationToken = default) =>
        (await dbContext.Courses
            .AsNoTracking()
            .Include(item => item.Weekdays)
            .OrderBy(item => item.Name)
            .ToListAsync(cancellationToken))
        .Select(ToDto)
        .ToArray();

    public async Task<CourseDto?> GetCourseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var course = await dbContext.Courses
            .AsNoTracking()
            .Include(item => item.Weekdays)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return course is null ? null : ToDto(course);
    }

    public async Task<CourseDto> CreateCourseAsync(
        SaveCourseCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCourse(command);
        var name = command.Name.Trim();
        if (await dbContext.Courses.AnyAsync(item => item.Name == name, cancellationToken))
        {
            throw new PlanningConflictException($"A course named '{name}' already exists.");
        }

        var course = new Course
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = command.Description?.Trim() ?? string.Empty,
            Weekdays = NormalizeWeekdays(command.Weekdays)
                .Select(day => new CourseWeekday { Weekday = day })
                .ToList()
        };
        dbContext.Courses.Add(course);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(course);
    }

    public async Task<CourseDto?> UpdateCourseAsync(
        Guid id,
        SaveCourseCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCourse(command);
        var course = await dbContext.Courses
            .Include(item => item.Weekdays)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (course is null)
        {
            return null;
        }

        var name = command.Name.Trim();
        if (await dbContext.Courses.AnyAsync(item => item.Id != id && item.Name == name, cancellationToken))
        {
            throw new PlanningConflictException($"A course named '{name}' already exists.");
        }

        var desired = NormalizeWeekdays(command.Weekdays).ToHashSet();
        var removed = course.Weekdays.Select(item => item.Weekday).Where(day => !desired.Contains(day)).ToHashSet();
        if (removed.Count > 0)
        {
            var assignedDates = await dbContext.TopicAssignments
                .Where(item => item.CourseId == id)
                .Select(item => item.Date)
                .ToListAsync(cancellationToken);
            if (assignedDates.Any(date => removed.Contains(ToIsoWeekday(date))))
            {
                throw new PlanningConflictException(
                    "A teaching weekday cannot be removed while topics are scheduled on that weekday.");
            }
        }

        foreach (var weekday in course.Weekdays.Where(item => !desired.Contains(item.Weekday)).ToArray())
        {
            course.Weekdays.Remove(weekday);
        }
        foreach (var weekday in desired.Except(course.Weekdays.Select(item => item.Weekday)))
        {
            course.Weekdays.Add(new CourseWeekday { CourseId = id, Weekday = weekday });
        }

        course.Name = name;
        course.Description = command.Description?.Trim() ?? string.Empty;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(course);
    }

    public async Task<bool> DeleteCourseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var course = await dbContext.Courses.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (course is null)
        {
            return false;
        }

        dbContext.Courses.Remove(course);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<GlobalDayMarkerDto>> GetGlobalMarkersAsync(
        CancellationToken cancellationToken = default) =>
        (await dbContext.GlobalDayMarkers.AsNoTracking().OrderBy(item => item.Date).ToListAsync(cancellationToken))
        .Select(ToDto)
        .ToArray();

    public async Task<IReadOnlyList<CourseExamDto>> GetCourseExamsAsync(
        Guid? courseId,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.CourseExams.AsNoTracking().AsQueryable();
        if (courseId.HasValue)
        {
            query = query.Where(item => item.CourseId == courseId.Value);
        }

        return (await query.OrderBy(item => item.Date).ToListAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<CalendarViewDto> GetCalendarAsync(
        Guid? courseId,
        CancellationToken cancellationToken = default)
    {
        var config = ToDto(await GetConfigEntityAsync(cancellationToken));
        var courseWeekdays = new HashSet<IsoWeekday>();
        if (courseId.HasValue)
        {
            var courseExists = await dbContext.Courses.AnyAsync(item => item.Id == courseId, cancellationToken);
            if (!courseExists)
            {
                throw new KeyNotFoundException("Course not found.");
            }

            courseWeekdays = (await dbContext.CourseWeekdays
                .Where(item => item.CourseId == courseId)
                .Select(item => item.Weekday)
                .ToListAsync(cancellationToken))
                .ToHashSet();
        }

        var markers = (await dbContext.GlobalDayMarkers
                .AsNoTracking()
                .Where(item => item.Date >= config.PlanningStart && item.Date <= config.PlanningEnd)
                .ToListAsync(cancellationToken))
            .Select(ToDto)
            .ToDictionary(item => item.Date);

        var exams = courseId.HasValue
            ? (await dbContext.CourseExams
                    .AsNoTracking()
                    .Where(item => item.CourseId == courseId && item.Date >= config.PlanningStart && item.Date <= config.PlanningEnd)
                    .ToListAsync(cancellationToken))
                .Select(ToDto)
                .ToDictionary(item => item.Date)
            : new Dictionary<DateOnly, CourseExamDto>();

        return CalendarBuilder.Build(config, courseId, courseWeekdays, markers, exams);
    }

    private async Task<AppConfig> GetConfigEntityAsync(CancellationToken cancellationToken) =>
        await dbContext.AppConfigs.SingleAsync(item => item.Id == AppConfig.SingletonId, cancellationToken);

    private static void ValidateConfig(UpdateAppConfigCommand command)
    {
        if (command.PlanningEnd < command.PlanningStart)
        {
            throw new ArgumentException("Planning end must be on or after planning start.");
        }

        if (NormalizeWeekdays(command.VisibleWeekdays).Count == 0)
        {
            throw new ArgumentException("Select at least one visible weekday.");
        }

        ValidateColor(command.HolidayColor, "Holiday color");
        ValidateColor(command.EventColor, "Event color");
        ValidateColor(command.ExamColor, "Exam color");
    }

    private static void ValidateCourse(SaveCourseCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 100)
        {
            throw new ArgumentException("Course name is required and must not exceed 100 characters.");
        }

        if (command.Description?.Length > 2000)
        {
            throw new ArgumentException("Course description must not exceed 2000 characters.");
        }

        if (NormalizeWeekdays(command.Weekdays).Count == 0)
        {
            throw new ArgumentException("Select at least one teaching weekday.");
        }
    }

    private static void ValidateColor(string color, string field)
    {
        if (string.IsNullOrWhiteSpace(color) || color.Trim().Length > 20)
        {
            throw new ArgumentException($"{field} is required and must not exceed 20 characters.");
        }
    }

    private static IReadOnlyList<IsoWeekday> NormalizeWeekdays(IEnumerable<IsoWeekday>? weekdays) =>
        (weekdays ?? []).Where(day => day is >= IsoWeekday.Monday and <= IsoWeekday.Sunday).Distinct().Order().ToArray();

    private static int ToMask(IEnumerable<IsoWeekday>? weekdays) => NormalizeWeekdays(weekdays)
        .Aggregate(0, (mask, day) => mask | 1 << ((int)day - 1));

    private static IReadOnlyList<IsoWeekday> FromMask(int mask) => Enum.GetValues<IsoWeekday>()
        .Where(day => (mask & 1 << ((int)day - 1)) != 0)
        .ToArray();

    private static IsoWeekday ToIsoWeekday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday
        ? IsoWeekday.Sunday
        : (IsoWeekday)date.DayOfWeek;

    private static AppConfigDto ToDto(AppConfig config) => new(
        config.PlanningStart,
        config.PlanningEnd,
        FromMask(config.VisibleWeekdaysMask),
        config.HolidayColor,
        config.EventColor,
        config.ExamColor);

    private static CourseDto ToDto(Course course) => new(
        course.Id,
        course.Name,
        course.Description,
        course.Weekdays.Select(item => item.Weekday).Order().ToArray());

    public static GlobalDayMarkerDto ToDto(GlobalDayMarker marker) => new(marker.Id, marker.Date, marker.Type, marker.Label);
    public static CourseExamDto ToDto(CourseExam exam) => new(exam.Id, exam.CourseId, exam.Date, exam.Name);
}
