using WeeklyLessonPlanner.Core.Calendar;

namespace WeeklyLessonPlanner.Infrastructure.Persistence;

public sealed class AppConfig
{
    public const int SingletonId = 1;
    public int Id { get; set; } = SingletonId;
    public int VisibleWeekdaysMask { get; set; }
    public string HolidayColor { get; set; } = "#2e7d32";
    public string EventColor { get; set; } = "#1565c0";
    public string ExamColor { get; set; } = "#ed6c02";
    public uint Version { get; set; }
}

public sealed class SchoolYear
{
    public static readonly Guid DefaultId = Guid.Parse("6f708a97-c4e2-4a72-9652-aaf16f983d3f");
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateOnly PlanningStart { get; set; }
    public DateOnly PlanningEnd { get; set; }
    public uint Version { get; set; }
    public ICollection<Course> Courses { get; set; } = [];
    public ICollection<GlobalDayMarker> GlobalDayMarkers { get; set; } = [];
}

public sealed class Course
{
    public Guid Id { get; set; }
    public Guid SchoolYearId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public uint Version { get; set; }
    public SchoolYear SchoolYear { get; set; } = null!;
    public ICollection<CourseWeekday> Weekdays { get; set; } = [];
    public ICollection<CourseExam> Exams { get; set; } = [];
    public ICollection<Topic> Topics { get; set; } = [];
}

public sealed class CourseWeekday
{
    public Guid CourseId { get; set; }
    public IsoWeekday Weekday { get; set; }
    public Course Course { get; set; } = null!;
}

public sealed class GlobalDayMarker
{
    public Guid Id { get; set; }
    public Guid SchoolYearId { get; set; }
    public DateOnly Date { get; set; }
    public GlobalDayMarkerType Type { get; set; }
    public string? Label { get; set; }
    public uint Version { get; set; }
    public SchoolYear SchoolYear { get; set; } = null!;
}

public sealed class CourseExam
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public DateOnly Date { get; set; }
    public string Name { get; set; } = string.Empty;
    public uint Version { get; set; }
    public Course Course { get; set; } = null!;
}

public sealed class Topic
{
    public Guid Id { get; set; }
    public Guid CourseId { get; set; }
    public string Heading { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public uint Version { get; set; }
    public Course Course { get; set; } = null!;
    public ICollection<TopicInstance> Instances { get; set; } = [];
}

public sealed class TopicInstance
{
    public Guid Id { get; set; }
    public Guid TopicId { get; set; }
    public Guid CourseId { get; set; }
    public uint Version { get; set; }
    public Topic Topic { get; set; } = null!;
    public TopicAssignment? Assignment { get; set; }
}

public sealed class TopicAssignment
{
    public Guid Id { get; set; }
    public Guid TopicInstanceId { get; set; }
    public Guid CourseId { get; set; }
    public DateOnly Date { get; set; }
    public uint Version { get; set; }
    public TopicInstance TopicInstance { get; set; } = null!;
}
