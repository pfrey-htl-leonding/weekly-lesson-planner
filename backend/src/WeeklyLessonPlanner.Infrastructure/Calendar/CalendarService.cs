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
        config.VisibleWeekdaysMask = ToMask(command.VisibleWeekdays);
        config.HolidayColor = command.HolidayColor.Trim();
        config.EventColor = command.EventColor.Trim();
        config.ExamColor = command.ExamColor.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(config);
    }

    public async Task<IReadOnlyList<SchoolYearDto>> GetSchoolYearsAsync(
        CancellationToken cancellationToken = default) =>
        (await dbContext.SchoolYears.AsNoTracking()
            .OrderBy(item => item.PlanningStart)
            .ThenBy(item => item.Name)
            .ToListAsync(cancellationToken))
        .Select(ToDto)
        .ToArray();

    public async Task<SchoolYearDto> CreateSchoolYearAsync(
        SaveSchoolYearCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateSchoolYear(command);
        var name = command.Name.Trim();
        if (await dbContext.SchoolYears.AnyAsync(item => item.Name == name, cancellationToken))
        {
            throw new PlanningConflictException($"A school year named '{name}' already exists.");
        }

        var schoolYear = new SchoolYear
        {
            Id = Guid.NewGuid(),
            Name = name,
            PlanningStart = command.PlanningStart,
            PlanningEnd = command.PlanningEnd
        };
        dbContext.SchoolYears.Add(schoolYear);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(schoolYear);
    }

    public async Task<SchoolYearDto?> UpdateSchoolYearAsync(
        Guid id,
        SaveSchoolYearCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateSchoolYear(command);
        var schoolYear = await dbContext.SchoolYears.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (schoolYear is null) return null;
        var name = command.Name.Trim();
        if (await dbContext.SchoolYears.AnyAsync(item => item.Id != id && item.Name == name, cancellationToken))
        {
            throw new PlanningConflictException($"A school year named '{name}' already exists.");
        }

        var hasDatesOutsideRange = await dbContext.TopicAssignments.AnyAsync(
                item => item.TopicInstance.Topic.Course.SchoolYearId == id &&
                    (item.Date < command.PlanningStart || item.Date > command.PlanningEnd), cancellationToken) ||
            await dbContext.CourseExams.AnyAsync(
                item => item.Course.SchoolYearId == id &&
                    (item.Date < command.PlanningStart || item.Date > command.PlanningEnd), cancellationToken) ||
            await dbContext.GlobalDayMarkers.AnyAsync(
                item => item.SchoolYearId == id &&
                    (item.Date < command.PlanningStart || item.Date > command.PlanningEnd), cancellationToken);
        if (hasDatesOutsideRange)
        {
            throw new PlanningConflictException(
                "The school-year range cannot exclude scheduled topics or fixed days. Move or remove them first.");
        }

        schoolYear.Name = name;
        schoolYear.PlanningStart = command.PlanningStart;
        schoolYear.PlanningEnd = command.PlanningEnd;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(schoolYear);
    }

    public async Task<bool> DeleteSchoolYearAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var schoolYear = await dbContext.SchoolYears.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (schoolYear is null) return false;
        dbContext.SchoolYears.Remove(schoolYear);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CourseDto>> GetCoursesAsync(
        Guid? schoolYearId = null,
        CancellationToken cancellationToken = default)
    {
        var query = dbContext.Courses.AsNoTracking().Include(item => item.Weekdays).AsQueryable();
        if (schoolYearId.HasValue) query = query.Where(item => item.SchoolYearId == schoolYearId);
        return (await query.OrderBy(item => item.Name).ToListAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<CourseDto?> GetCourseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var course = await dbContext.Courses.AsNoTracking().Include(item => item.Weekdays)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        return course is null ? null : ToDto(course);
    }

    public async Task<CourseDto> CreateCourseAsync(
        SaveCourseCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateCourse(command);
        if (!await dbContext.SchoolYears.AnyAsync(item => item.Id == command.SchoolYearId, cancellationToken))
            throw new KeyNotFoundException("School year not found.");
        var name = command.Name.Trim();
        if (await dbContext.Courses.AnyAsync(
                item => item.SchoolYearId == command.SchoolYearId && item.Name == name, cancellationToken))
            throw new PlanningConflictException($"A course named '{name}' already exists in this school year.");

        var course = new Course
        {
            Id = Guid.NewGuid(),
            SchoolYearId = command.SchoolYearId,
            Name = name,
            Description = command.Description?.Trim() ?? string.Empty,
            Weekdays = NormalizeWeekdays(command.Weekdays).Select(day => new CourseWeekday { Weekday = day }).ToList()
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
        var course = await dbContext.Courses.Include(item => item.Weekdays)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (course is null) return null;
        if (course.SchoolYearId != command.SchoolYearId)
            throw new PlanningConflictException("An existing course cannot be moved to another school year. Clone it instead.");

        var name = command.Name.Trim();
        if (await dbContext.Courses.AnyAsync(item => item.Id != id && item.SchoolYearId == command.SchoolYearId && item.Name == name, cancellationToken))
            throw new PlanningConflictException($"A course named '{name}' already exists in this school year.");

        var desired = NormalizeWeekdays(command.Weekdays).ToHashSet();
        var removed = course.Weekdays.Select(item => item.Weekday).Where(day => !desired.Contains(day)).ToHashSet();
        if (removed.Count > 0)
        {
            var assignedDates = await dbContext.TopicAssignments.Where(item => item.CourseId == id)
                .Select(item => item.Date).ToListAsync(cancellationToken);
            if (assignedDates.Any(date => removed.Contains(ToIsoWeekday(date))))
                throw new PlanningConflictException("A teaching weekday cannot be removed while topics are scheduled on that weekday.");
        }

        foreach (var weekday in course.Weekdays.Where(item => !desired.Contains(item.Weekday)).ToArray())
            course.Weekdays.Remove(weekday);
        foreach (var weekday in desired.Except(course.Weekdays.Select(item => item.Weekday)))
            course.Weekdays.Add(new CourseWeekday { CourseId = id, Weekday = weekday });
        course.Name = name;
        course.Description = command.Description?.Trim() ?? string.Empty;
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(course);
    }

    public async Task<bool> DeleteCourseAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var course = await dbContext.Courses.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (course is null) return false;
        dbContext.Courses.Remove(course);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<GlobalDayMarkerDto>> GetGlobalMarkersAsync(
        Guid schoolYearId,
        CancellationToken cancellationToken = default) =>
        (await dbContext.GlobalDayMarkers.AsNoTracking()
            .Where(item => item.SchoolYearId == schoolYearId)
            .OrderBy(item => item.Date).ToListAsync(cancellationToken))
        .Select(ToDto).ToArray();

    public async Task<IReadOnlyList<CourseExamDto>> GetCourseExamsAsync(Guid? courseId, CancellationToken cancellationToken = default)
    {
        var query = dbContext.CourseExams.AsNoTracking().AsQueryable();
        if (courseId.HasValue) query = query.Where(item => item.CourseId == courseId.Value);
        return (await query.OrderBy(item => item.Date).ToListAsync(cancellationToken)).Select(ToDto).ToArray();
    }

    public async Task<CalendarViewDto> GetCalendarAsync(
        Guid? courseId,
        Guid? schoolYearId,
        CancellationToken cancellationToken = default)
    {
        Course? course = null;
        if (courseId.HasValue)
        {
            course = await dbContext.Courses.AsNoTracking().Include(item => item.Weekdays)
                .Include(item => item.SchoolYear).SingleOrDefaultAsync(item => item.Id == courseId, cancellationToken)
                ?? throw new KeyNotFoundException("Course not found.");
            schoolYearId = course.SchoolYearId;
        }

        var schoolYear = schoolYearId.HasValue
            ? await dbContext.SchoolYears.AsNoTracking().SingleOrDefaultAsync(item => item.Id == schoolYearId, cancellationToken)
            : await dbContext.SchoolYears.AsNoTracking().OrderBy(item => item.PlanningStart).FirstOrDefaultAsync(cancellationToken);
        if (schoolYear is null) throw new KeyNotFoundException("School year not found.");
        var config = ToDto(await GetConfigEntityAsync(cancellationToken));
        var courseWeekdays = course?.Weekdays.Select(item => item.Weekday).ToHashSet() ?? [];

        var markers = (await dbContext.GlobalDayMarkers.AsNoTracking()
            .Where(item => item.SchoolYearId == schoolYear.Id)
            .ToListAsync(cancellationToken)).Select(ToDto).ToDictionary(item => item.Date);
        var examQuery = dbContext.CourseExams.AsNoTracking()
            .Where(item => item.Course.SchoolYearId == schoolYear.Id &&
                item.Date >= schoolYear.PlanningStart && item.Date <= schoolYear.PlanningEnd);
        if (courseId.HasValue) examQuery = examQuery.Where(item => item.CourseId == courseId.Value);
        var calendarExams = await examQuery
            .OrderBy(item => item.Course.Name)
            .ThenBy(item => item.Name)
            .Select(item => new
            {
                item.Date,
                Exam = new CourseExamDto(item.Id, item.CourseId, item.Date, item.Name),
                ScheduledExam = new ScheduledExamDto(item.Id, item.CourseId, item.Course.Name, item.Name)
            })
            .ToListAsync(cancellationToken);
        var exams = courseId.HasValue
            ? calendarExams.ToDictionary(item => item.Date, item => item.Exam)
            : new Dictionary<DateOnly, CourseExamDto>();
        var scheduledExams = calendarExams.GroupBy(item => item.Date)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<ScheduledExamDto>)group.Select(item => item.ScheduledExam).ToArray());
        var assignmentQuery = dbContext.TopicAssignments.AsNoTracking()
            .Where(item => item.Date >= schoolYear.PlanningStart && item.Date <= schoolYear.PlanningEnd &&
                item.TopicInstance.Topic.Course.SchoolYearId == schoolYear.Id);
        if (courseId.HasValue) assignmentQuery = assignmentQuery.Where(item => item.CourseId == courseId.Value);
        var assignments = await assignmentQuery.OrderBy(item => item.TopicInstance.Topic.Course.Name)
            .ThenBy(item => item.TopicInstance.Topic.Heading)
            .Select(item => new
            {
                item.Date,
                Topic = new ScheduledTopicDto(item.Id, item.TopicInstanceId, item.TopicInstance.TopicId, item.CourseId,
                    item.TopicInstance.Topic.Course.Name, item.TopicInstance.Topic.Heading, item.TopicInstance.Topic.Description)
            }).ToListAsync(cancellationToken);
        var scheduledTopics = assignments.GroupBy(item => item.Date)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ScheduledTopicDto>)group.Select(item => item.Topic).ToArray());

        var planningSummary = course is null
            ? null
            : new CoursePlanningSummaryDto(
                CountLessonDays(
                    schoolYear.PlanningStart,
                    schoolYear.PlanningEnd,
                    courseWeekdays,
                    markers.Keys,
                    exams.Keys),
                assignments.Count,
                await dbContext.TopicInstances.AsNoTracking()
                    .CountAsync(item => item.CourseId == course.Id && item.Assignment == null, cancellationToken));

        return CalendarBuilder.Build(
            ToDto(schoolYear), config, courseId, courseWeekdays, markers, exams, scheduledTopics, scheduledExams) with
        {
            PlanningSummary = planningSummary
        };
    }

    private static int CountLessonDays(
        DateOnly planningStart,
        DateOnly planningEnd,
        IReadOnlySet<IsoWeekday> courseWeekdays,
        IEnumerable<DateOnly> markerDates,
        IEnumerable<DateOnly> examDates)
    {
        var fixedDates = markerDates.Concat(examDates).ToHashSet();
        var count = 0;
        for (var date = planningStart; date <= planningEnd; date = date.AddDays(1))
        {
            if (courseWeekdays.Contains(ToIsoWeekday(date)) && !fixedDates.Contains(date)) count++;
        }

        return count;
    }

    private async Task<AppConfig> GetConfigEntityAsync(CancellationToken cancellationToken) =>
        await dbContext.AppConfigs.SingleAsync(item => item.Id == AppConfig.SingletonId, cancellationToken);

    private static void ValidateConfig(UpdateAppConfigCommand command)
    {
        if (NormalizeWeekdays(command.VisibleWeekdays).Count == 0) throw new ArgumentException("Select at least one visible weekday.");
        ValidateColor(command.HolidayColor, "Holiday color");
        ValidateColor(command.EventColor, "Event color");
        ValidateColor(command.ExamColor, "Exam color");
    }

    private static void ValidateSchoolYear(SaveSchoolYearCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 100)
            throw new ArgumentException("School-year name is required and must not exceed 100 characters.");
        if (command.PlanningEnd < command.PlanningStart)
            throw new ArgumentException("Planning end must be on or after planning start.");
    }

    private static void ValidateCourse(SaveCourseCommand command)
    {
        if (command.SchoolYearId == Guid.Empty) throw new ArgumentException("Select a school year.");
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 100)
            throw new ArgumentException("Course name is required and must not exceed 100 characters.");
        if (command.Description?.Length > 2000) throw new ArgumentException("Course description must not exceed 2000 characters.");
        if (NormalizeWeekdays(command.Weekdays).Count == 0) throw new ArgumentException("Select at least one teaching weekday.");
    }

    private static void ValidateColor(string color, string field)
    {
        if (string.IsNullOrWhiteSpace(color) || color.Trim().Length > 20)
            throw new ArgumentException($"{field} is required and must not exceed 20 characters.");
    }

    private static IReadOnlyList<IsoWeekday> NormalizeWeekdays(IEnumerable<IsoWeekday>? weekdays) =>
        (weekdays ?? []).Where(day => day is >= IsoWeekday.Monday and <= IsoWeekday.Sunday).Distinct().Order().ToArray();
    private static int ToMask(IEnumerable<IsoWeekday>? weekdays) => NormalizeWeekdays(weekdays)
        .Aggregate(0, (mask, day) => mask | 1 << ((int)day - 1));
    private static IReadOnlyList<IsoWeekday> FromMask(int mask) => Enum.GetValues<IsoWeekday>()
        .Where(day => (mask & 1 << ((int)day - 1)) != 0).ToArray();
    private static IsoWeekday ToIsoWeekday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday
        ? IsoWeekday.Sunday : (IsoWeekday)date.DayOfWeek;

    private static AppConfigDto ToDto(AppConfig config) => new(
        FromMask(config.VisibleWeekdaysMask), config.HolidayColor, config.EventColor, config.ExamColor);
    public static SchoolYearDto ToDto(SchoolYear schoolYear) => new(
        schoolYear.Id, schoolYear.Name, schoolYear.PlanningStart, schoolYear.PlanningEnd);
    private static CourseDto ToDto(Course course) => new(
        course.Id, course.SchoolYearId, course.Name, course.Description,
        course.Weekdays.Select(item => item.Weekday).Order().ToArray());
    public static GlobalDayMarkerDto ToDto(GlobalDayMarker marker) => new(
        marker.Id, marker.SchoolYearId, marker.Date, marker.Type, marker.Label);
    public static CourseExamDto ToDto(CourseExam exam) => new(exam.Id, exam.CourseId, exam.Date, exam.Name);
}
