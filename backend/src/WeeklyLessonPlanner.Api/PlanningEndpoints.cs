using WeeklyLessonPlanner.Core.Planning;

namespace WeeklyLessonPlanner.Api;

public static class PlanningEndpoints
{
    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var planning = endpoints.MapGroup("/api/planning");

        planning.MapPost("/place", async (
            PlaceTopicCommand command,
            IPlanningService service,
            CancellationToken token) => Results.Ok(await service.PlaceTopicAsync(command, token)));

        planning.MapPost("/remove", async (
            RemoveTopicCommand command,
            IPlanningService service,
            CancellationToken token) =>
        {
            var impact = await service.RemoveTopicAsync(command, token);
            return impact is null ? Results.NotFound() : Results.Ok(impact);
        });

        planning.MapPost("/drag", async (
            DragTopicCommand command,
            IPlanningService service,
            CancellationToken token) =>
        {
            var impact = await service.DragTopicAsync(command, token);
            return impact is null ? Results.NotFound() : Results.Ok(impact);
        });

        planning.MapPost("/add-all", async (
            MultipleTopicPlanningCommand command,
            IPlanningService service,
            CancellationToken token) => Results.Ok(await service.AddAllTopicsAsync(command, token)));

        planning.MapPost("/remove-all", async (
            MultipleTopicPlanningCommand command,
            IPlanningService service,
            CancellationToken token) => Results.Ok(await service.RemoveAllTopicsAsync(command, token)));

        planning.MapPost("/move-exam", async (
            MoveCourseExamCommand command,
            IPlanningService service,
            CancellationToken token) => Results.Ok(await service.MoveCourseExamAsync(command, token)));

        planning.MapPost("/course-rollover", async (
            CourseRolloverCommand command,
            IPlanningService service,
            CancellationToken token) => Results.Ok(await service.RollOverCourseAsync(command, token)));

        return endpoints;
    }
}
