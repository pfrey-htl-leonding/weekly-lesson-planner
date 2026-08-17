using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;

namespace WeeklyLessonPlanner.Api;

public static class CalendarEndpoints
{
    public static IEndpointRouteBuilder MapCalendarEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapGroup("/api");

        api.MapGet("/config", (ICalendarService service, CancellationToken token) =>
            service.GetConfigAsync(token));
        api.MapPut("/config", (UpdateAppConfigCommand command, ICalendarService service, CancellationToken token) =>
            service.UpdateConfigAsync(command, token));

        var schoolYears = api.MapGroup("/school-years");
        schoolYears.MapGet("/", (ICalendarService service, CancellationToken token) => service.GetSchoolYearsAsync(token));
        schoolYears.MapPost("/", async (SaveSchoolYearCommand command, ICalendarService service, CancellationToken token) =>
        {
            var schoolYear = await service.CreateSchoolYearAsync(command, token);
            return Results.Created($"/api/school-years/{schoolYear.Id}", schoolYear);
        });
        schoolYears.MapPut("/{id:guid}", async (Guid id, SaveSchoolYearCommand command, ICalendarService service, CancellationToken token) =>
        {
            var schoolYear = await service.UpdateSchoolYearAsync(id, command, token);
            return schoolYear is null ? Results.NotFound() : Results.Ok(schoolYear);
        });
        schoolYears.MapDelete("/{id:guid}", async (Guid id, ICalendarService service, CancellationToken token) =>
            await service.DeleteSchoolYearAsync(id, token) ? Results.NoContent() : Results.NotFound());

        var courses = api.MapGroup("/courses");
        courses.MapGet("/", (Guid? schoolYearId, ICalendarService service, CancellationToken token) =>
            service.GetCoursesAsync(schoolYearId, token));
        courses.MapGet("/{id:guid}", async (Guid id, ICalendarService service, CancellationToken token) =>
        {
            var course = await service.GetCourseAsync(id, token);
            return course is null ? Results.NotFound() : Results.Ok(course);
        });
        courses.MapPost("/", async (SaveCourseCommand command, ICalendarService service, CancellationToken token) =>
        {
            var course = await service.CreateCourseAsync(command, token);
            return Results.Created($"/api/courses/{course.Id}", course);
        });
        courses.MapPut("/{id:guid}", async (Guid id, SaveCourseCommand command, ICalendarService service, CancellationToken token) =>
        {
            var course = await service.UpdateCourseAsync(id, command, token);
            return course is null ? Results.NotFound() : Results.Ok(course);
        });
        courses.MapDelete("/{id:guid}", async (Guid id, ICalendarService service, CancellationToken token) =>
            await service.DeleteCourseAsync(id, token) ? Results.NoContent() : Results.NotFound());

        var markers = api.MapGroup("/global-markers");
        markers.MapGet("/", (Guid schoolYearId, ICalendarService service, CancellationToken token) =>
            service.GetGlobalMarkersAsync(schoolYearId, token));
        markers.MapPost("/", async (SaveGlobalDayMarkerCommand command, IPlanningService service, CancellationToken token) =>
        {
            var marker = await service.CreateGlobalMarkerAsync(command, token);
            return Results.Created($"/api/global-markers/{marker.Id}", marker);
        });
        markers.MapPost("/range", async (
            SaveGlobalDayMarkerRangeCommand command,
            IPlanningService service,
            CancellationToken token) =>
        {
            var created = await service.CreateGlobalMarkerRangeAsync(command, token);
            return Results.Created("/api/global-markers", created);
        });
        markers.MapPut("/{id:guid}", async (Guid id, SaveGlobalDayMarkerCommand command, IPlanningService service, CancellationToken token) =>
        {
            var marker = await service.UpdateGlobalMarkerAsync(id, command, token);
            return marker is null ? Results.NotFound() : Results.Ok(marker);
        });
        markers.MapDelete("/{id:guid}", async (Guid id, IPlanningService service, CancellationToken token) =>
            await service.DeleteGlobalMarkerAsync(id, token) ? Results.NoContent() : Results.NotFound());

        var exams = api.MapGroup("/course-exams");
        exams.MapGet("/", (Guid? courseId, ICalendarService service, CancellationToken token) =>
            service.GetCourseExamsAsync(courseId, token));
        exams.MapPost("/", async (SaveCourseExamCommand command, IPlanningService service, CancellationToken token) =>
        {
            var exam = await service.CreateCourseExamAsync(command, token);
            return Results.Created($"/api/course-exams/{exam.Id}", exam);
        });
        exams.MapPut("/{id:guid}", async (Guid id, SaveCourseExamCommand command, IPlanningService service, CancellationToken token) =>
        {
            var exam = await service.UpdateCourseExamAsync(id, command, token);
            return exam is null ? Results.NotFound() : Results.Ok(exam);
        });
        exams.MapDelete("/{id:guid}", async (Guid id, IPlanningService service, CancellationToken token) =>
            await service.DeleteCourseExamAsync(id, token) ? Results.NoContent() : Results.NotFound());

        api.MapGet("/calendar", (Guid? courseId, Guid? schoolYearId, ICalendarService service, CancellationToken token) =>
            service.GetCalendarAsync(courseId, schoolYearId, token));

        return endpoints;
    }
}
