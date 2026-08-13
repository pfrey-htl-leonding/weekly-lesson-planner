namespace WeeklyLessonPlanner.Core.Calendar;

public interface ICalendarService
{
    Task<AppConfigDto> GetConfigAsync(CancellationToken cancellationToken = default);
    Task<AppConfigDto> UpdateConfigAsync(UpdateAppConfigCommand command, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CourseDto>> GetCoursesAsync(CancellationToken cancellationToken = default);
    Task<CourseDto?> GetCourseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<CourseDto> CreateCourseAsync(SaveCourseCommand command, CancellationToken cancellationToken = default);
    Task<CourseDto?> UpdateCourseAsync(Guid id, SaveCourseCommand command, CancellationToken cancellationToken = default);
    Task<bool> DeleteCourseAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GlobalDayMarkerDto>> GetGlobalMarkersAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CourseExamDto>> GetCourseExamsAsync(Guid? courseId, CancellationToken cancellationToken = default);
    Task<CalendarViewDto> GetCalendarAsync(Guid? courseId, CancellationToken cancellationToken = default);
}
