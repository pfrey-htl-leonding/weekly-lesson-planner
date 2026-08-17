using Microsoft.EntityFrameworkCore;

namespace WeeklyLessonPlanner.Infrastructure.Persistence;

public sealed class PlannerDbContext(DbContextOptions<PlannerDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseMetadata> DatabaseMetadata => Set<DatabaseMetadata>();
    public DbSet<AppConfig> AppConfigs => Set<AppConfig>();
    public DbSet<SchoolYear> SchoolYears => Set<SchoolYear>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseWeekday> CourseWeekdays => Set<CourseWeekday>();
    public DbSet<GlobalDayMarker> GlobalDayMarkers => Set<GlobalDayMarker>();
    public DbSet<CourseExam> CourseExams => Set<CourseExam>();
    public DbSet<Topic> Topics => Set<Topic>();
    public DbSet<TopicInstance> TopicInstances => Set<TopicInstance>();
    public DbSet<TopicAssignment> TopicAssignments => Set<TopicAssignment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var metadata = modelBuilder.Entity<DatabaseMetadata>();

        metadata.ToTable("database_metadata");
        metadata.HasKey(item => item.Id);
        metadata.HasIndex(item => item.Key).IsUnique();
        metadata.Property(item => item.Key).HasMaxLength(100);
        metadata.Property(item => item.Value).HasMaxLength(500);
        metadata.Property(item => item.RecordedOn).HasColumnType("date");
        metadata.Property(item => item.Version).IsRowVersion();

        ConfigureAppConfig(modelBuilder);
        ConfigureSchoolYear(modelBuilder);
        ConfigureCourse(modelBuilder);
        ConfigureMarkers(modelBuilder);
        ConfigureTopics(modelBuilder);
    }

    private static void ConfigureAppConfig(ModelBuilder modelBuilder)
    {
        var config = modelBuilder.Entity<AppConfig>();
        config.ToTable("app_config", table =>
        {
            table.HasCheckConstraint("ck_app_config_singleton", "\"Id\" = 1");
            table.HasCheckConstraint("ck_app_config_weekdays", "\"VisibleWeekdaysMask\" BETWEEN 1 AND 127");
        });
        config.HasKey(item => item.Id);
        config.Property(item => item.HolidayColor).HasMaxLength(20);
        config.Property(item => item.EventColor).HasMaxLength(20);
        config.Property(item => item.ExamColor).HasMaxLength(20);
        config.Property(item => item.Version).IsRowVersion();
        config.HasData(new AppConfig
        {
            Id = AppConfig.SingletonId,
            VisibleWeekdaysMask = 31,
            HolidayColor = "#2e7d32",
            EventColor = "#1565c0",
            ExamColor = "#ed6c02"
        });
    }

    private static void ConfigureSchoolYear(ModelBuilder modelBuilder)
    {
        var schoolYear = modelBuilder.Entity<SchoolYear>();
        schoolYear.ToTable("school_years", table =>
            table.HasCheckConstraint("ck_school_year_range", "\"PlanningStart\" <= \"PlanningEnd\""));
        schoolYear.HasKey(item => item.Id);
        schoolYear.HasIndex(item => item.Name).IsUnique();
        schoolYear.Property(item => item.Name).HasMaxLength(100);
        schoolYear.Property(item => item.PlanningStart).HasColumnType("date");
        schoolYear.Property(item => item.PlanningEnd).HasColumnType("date");
        schoolYear.Property(item => item.Version).IsRowVersion();
        schoolYear.HasData(new SchoolYear
        {
            Id = SchoolYear.DefaultId,
            Name = "2026/27",
            PlanningStart = new DateOnly(2026, 9, 1),
            PlanningEnd = new DateOnly(2027, 6, 30)
        });
    }

    private static void ConfigureCourse(ModelBuilder modelBuilder)
    {
        var course = modelBuilder.Entity<Course>();
        course.ToTable("courses");
        course.HasKey(item => item.Id);
        course.HasIndex(item => new { item.SchoolYearId, item.Name }).IsUnique();
        course.Property(item => item.Name).HasMaxLength(100);
        course.Property(item => item.Description).HasMaxLength(2000);
        course.Property(item => item.Version).IsRowVersion();
        course.HasOne(item => item.SchoolYear)
            .WithMany(item => item.Courses)
            .HasForeignKey(item => item.SchoolYearId)
            .OnDelete(DeleteBehavior.Cascade);

        var weekday = modelBuilder.Entity<CourseWeekday>();
        weekday.ToTable("course_weekdays", table =>
            table.HasCheckConstraint("ck_course_weekday_value", "\"Weekday\" BETWEEN 1 AND 7"));
        weekday.HasKey(item => new { item.CourseId, item.Weekday });
        weekday.HasOne(item => item.Course)
            .WithMany(item => item.Weekdays)
            .HasForeignKey(item => item.CourseId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureMarkers(ModelBuilder modelBuilder)
    {
        var marker = modelBuilder.Entity<GlobalDayMarker>();
        marker.ToTable("global_day_markers", table =>
            table.HasCheckConstraint("ck_global_marker_type", "\"Type\" IN (1, 2)"));
        marker.HasKey(item => item.Id);
        marker.HasIndex(item => new { item.SchoolYearId, item.Date }).IsUnique();
        marker.Property(item => item.Date).HasColumnType("date");
        marker.Property(item => item.Label).HasMaxLength(200);
        marker.Property(item => item.Version).IsRowVersion();
        marker.HasOne(item => item.SchoolYear)
            .WithMany(item => item.GlobalDayMarkers)
            .HasForeignKey(item => item.SchoolYearId)
            .OnDelete(DeleteBehavior.Cascade);

        var exam = modelBuilder.Entity<CourseExam>();
        exam.ToTable("course_exams");
        exam.HasKey(item => item.Id);
        exam.HasIndex(item => new { item.CourseId, item.Date }).IsUnique();
        exam.HasIndex(item => item.Date);
        exam.Property(item => item.Date).HasColumnType("date");
        exam.Property(item => item.Name).HasMaxLength(200);
        exam.Property(item => item.Version).IsRowVersion();
        exam.HasOne(item => item.Course)
            .WithMany(item => item.Exams)
            .HasForeignKey(item => item.CourseId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureTopics(ModelBuilder modelBuilder)
    {
        var topic = modelBuilder.Entity<Topic>();
        topic.ToTable("topics");
        topic.HasKey(item => item.Id);
        topic.HasAlternateKey(item => new { item.Id, item.CourseId });
        topic.HasIndex(item => new { item.CourseId, item.Heading });
        topic.Property(item => item.Heading).HasMaxLength(200);
        topic.Property(item => item.Description).HasMaxLength(4000);
        topic.Property(item => item.Version).IsRowVersion();
        topic.HasOne(item => item.Course)
            .WithMany(item => item.Topics)
            .HasForeignKey(item => item.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        var instance = modelBuilder.Entity<TopicInstance>();
        instance.ToTable("topic_instances");
        instance.HasKey(item => item.Id);
        instance.HasAlternateKey(item => new { item.Id, item.CourseId });
        instance.Property(item => item.Version).IsRowVersion();
        instance.HasOne(item => item.Topic)
            .WithMany(item => item.Instances)
            .HasForeignKey(item => new { item.TopicId, item.CourseId })
            .HasPrincipalKey(item => new { item.Id, item.CourseId })
            .OnDelete(DeleteBehavior.Cascade);

        var assignment = modelBuilder.Entity<TopicAssignment>();
        assignment.ToTable("topic_assignments");
        assignment.HasKey(item => item.Id);
        assignment.HasIndex(item => item.TopicInstanceId).IsUnique();
        assignment.HasIndex(item => new { item.CourseId, item.Date }).IsUnique();
        assignment.Property(item => item.Date).HasColumnType("date");
        assignment.Property(item => item.Version).IsRowVersion();
        assignment.HasOne(item => item.TopicInstance)
            .WithOne(item => item.Assignment)
            .HasForeignKey<TopicAssignment>(item => new { item.TopicInstanceId, item.CourseId })
            .HasPrincipalKey<TopicInstance>(item => new { item.Id, item.CourseId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
