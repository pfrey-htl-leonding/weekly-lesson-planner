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

        var courses = api.MapGroup("/courses");
        courses.MapGet("/", (ICalendarService service, CancellationToken token) => service.GetCoursesAsync(token));
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
        markers.MapGet("/", (ICalendarService service, CancellationToken token) => service.GetGlobalMarkersAsync(token));
        markers.MapPost("/", async (SaveGlobalDayMarkerCommand command, IPlanningService service, CancellationToken token) =>
        {
            var marker = await service.CreateGlobalMarkerAsync(command, token);
            return Results.Created($"/api/global-markers/{marker.Id}", marker);
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

        api.MapGet("/calendar", (Guid? courseId, ICalendarService service, CancellationToken token) =>
            service.GetCalendarAsync(courseId, token));

        return endpoints;
    }
}
