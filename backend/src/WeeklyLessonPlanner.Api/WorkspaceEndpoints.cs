using WeeklyLessonPlanner.Infrastructure.Workspaces;

namespace WeeklyLessonPlanner.Api;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var workspaces = endpoints.MapGroup("/api/workspaces").WithTags("Workspaces");

        workspaces.MapGet("/", (IWorkspaceCatalog catalog, CancellationToken token) =>
            catalog.ListAsync(token));

        workspaces.MapGet("/{slug}", async (
            string slug,
            IWorkspaceCatalog catalog,
            CancellationToken token) =>
        {
            var workspace = await catalog.FindBySlugAsync(slug, token);
            return workspace is null
                ? Results.NotFound()
                : Results.Ok(new WorkspaceSummary(
                    workspace.Id,
                    workspace.Slug,
                    workspace.DisplayName,
                    workspace.Status.ToString().ToLowerInvariant(),
                    workspace.ProvisioningError));
        });

        workspaces.MapPost("/", async (
            CreateWorkspaceCommand command,
            IWorkspaceCatalog catalog,
            CancellationToken token) =>
        {
            var workspace = await catalog.CreateAsync(command, token);
            return Results.Created($"/api/workspaces/{workspace.Slug}", workspace);
        });

        workspaces.MapPost("/{slug}/retry-provisioning", async (
            string slug,
            IWorkspaceCatalog catalog,
            CancellationToken token) =>
            Results.Ok(await catalog.RetryAsync(slug, token)));

        workspaces.MapDelete("/{slug}", async (
            string slug,
            IWorkspaceCatalog catalog,
            CancellationToken token) =>
            await catalog.DeleteAsync(slug, token)
                ? Results.NoContent()
                : Results.NotFound());

        return endpoints;
    }
}

public sealed class WorkspaceResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context,
        IWorkspaceCatalog catalog,
        WorkspaceContextAccessor accessor,
        ILogger<WorkspaceResolutionMiddleware> logger)
    {
        if (context.Request.RouteValues.TryGetValue("workspaceSlug", out var value) &&
            value is string slug)
        {
            var workspace = await catalog.FindBySlugAsync(slug, context.RequestAborted);
            if (workspace is null)
            {
                await WriteProblemAsync(context, StatusCodes.Status404NotFound,
                    "Workspace not found", "The requested workspace does not exist.");
                return;
            }

            if (workspace.Status != WorkspaceStatus.Ready)
            {
                await WriteProblemAsync(context, StatusCodes.Status503ServiceUnavailable,
                    "Workspace unavailable", "The requested workspace is not ready.");
                return;
            }

            accessor.Set(workspace);
            using (logger.BeginScope(new Dictionary<string, object>
            {
                ["WorkspaceId"] = workspace.Id,
                ["WorkspaceSlug"] = workspace.Slug
            }))
            {
                await next(context);
            }
            return;
        }

        await next(context);
    }

    private static Task WriteProblemAsync(
        HttpContext context,
        int status,
        string title,
        string detail) =>
        Results.Problem(statusCode: status, title: title, detail: detail)
            .ExecuteAsync(context);
}
