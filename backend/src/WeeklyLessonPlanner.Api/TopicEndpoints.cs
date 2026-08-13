using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Core.Topics;

namespace WeeklyLessonPlanner.Api;

public static class TopicEndpoints
{
    public static IEndpointRouteBuilder MapTopicEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var topics = endpoints.MapGroup("/api/topics");
        topics.MapGet("/", (Guid? courseId, ITopicService service, CancellationToken token) =>
            service.GetTopicsAsync(courseId, token));
        topics.MapGet("/{id:guid}", async (Guid id, ITopicService service, CancellationToken token) =>
        {
            var topic = await service.GetTopicAsync(id, token);
            return topic is null ? Results.NotFound() : Results.Ok(topic);
        });
        topics.MapPost("/", async (SaveTopicCommand command, ITopicService service, CancellationToken token) =>
        {
            var topic = await service.CreateTopicAsync(command, token);
            return Results.Created($"/api/topics/{topic.Id}", topic);
        });
        topics.MapPut("/{id:guid}", async (
            Guid id,
            SaveTopicCommand command,
            ITopicService service,
            CancellationToken token) =>
        {
            var topic = await service.UpdateTopicAsync(id, command, token);
            return topic is null ? Results.NotFound() : Results.Ok(topic);
        });
        topics.MapDelete("/{id:guid}", async (Guid id, ITopicService service, CancellationToken token) =>
            await service.DeleteTopicAsync(id, token) ? Results.NoContent() : Results.NotFound());

        var instances = endpoints.MapGroup("/api/topic-instances");
        instances.MapGet("/unplanned", (
            Guid courseId,
            string? search,
            ITopicService service,
            CancellationToken token) => service.GetUnplannedInstancesAsync(courseId, search, token));
        instances.MapDelete("/{id:guid}", async (Guid id, ITopicService service, CancellationToken token) =>
            await service.DeleteUnplannedInstanceAsync(id, token) ? Results.NoContent() : Results.NotFound());
        instances.MapPost("/{id:guid}/copy", async (
            Guid id,
            IPlanningService service,
            CancellationToken token) =>
        {
            var copy = await service.CopyScheduledTopicAsync(id, token);
            return copy is null ? Results.NotFound() : Results.Created($"/api/topic-instances/{copy.Id}", copy);
        });

        return endpoints;
    }
}
