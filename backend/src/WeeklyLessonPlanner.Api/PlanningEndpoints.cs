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

        return endpoints;
    }
}
