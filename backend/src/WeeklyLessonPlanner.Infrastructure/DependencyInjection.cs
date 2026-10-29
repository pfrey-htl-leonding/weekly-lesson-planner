using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Core.Topics;
using WeeklyLessonPlanner.Infrastructure.Topics;
using WeeklyLessonPlanner.Infrastructure.Health;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Infrastructure.Planning;
using WeeklyLessonPlanner.Infrastructure.Workspaces;

namespace WeeklyLessonPlanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var fallbackConnectionString = configuration.GetConnectionString("PlannerDatabase");
        var runtimeConnectionString =
            configuration.GetConnectionString("PlannerRuntimeDatabase") ?? fallbackConnectionString;
        var ownerConnectionString =
            configuration.GetConnectionString("PlannerOwnerDatabase") ?? fallbackConnectionString;

        if (string.IsNullOrWhiteSpace(runtimeConnectionString) ||
            string.IsNullOrWhiteSpace(ownerConnectionString))
        {
            throw new InvalidOperationException(
                "Configure PlannerRuntimeDatabase and PlannerOwnerDatabase, " +
                "or the fallback ConnectionStrings:PlannerDatabase.");
        }

        var maximumCount = Math.Clamp(
            configuration.GetValue("Workspaces:MaximumCount", 25), 1, 1000);
        var maximumPoolSize = Math.Clamp(
            configuration.GetValue("Workspaces:MaximumPoolSizePerWorkspace", 5), 1, 100);
        var legacyWorkspaceSlug =
            configuration.GetValue("Workspaces:LegacyWorkspaceSlug", "pfrey") ?? "pfrey";
        var legacyWorkspaceDisplayName =
            configuration.GetValue("Workspaces:LegacyWorkspaceDisplayName", "pfrey") ?? "pfrey";
        var settings = new WorkspaceDatabaseSettings(
            runtimeConnectionString,
            ownerConnectionString,
            maximumCount,
            maximumPoolSize,
            configuration.GetValue("Workspaces:AllowCreate", true),
            legacyWorkspaceSlug,
            legacyWorkspaceDisplayName);

        services.AddSingleton(settings);
        services.AddSingleton<WorkspaceCatalog>();
        services.AddSingleton<IWorkspaceCatalog>(provider =>
            provider.GetRequiredService<WorkspaceCatalog>());
        services.AddSingleton<WorkspaceDatabaseInitializer>();
        services.AddScoped<WorkspaceContextAccessor>();

        services.AddDbContext<PlannerDbContext>((provider, options) =>
        {
            var workspace = provider.GetRequiredService<WorkspaceContextAccessor>().Current
                ?? throw new InvalidOperationException(
                    "A workspace must be resolved before PlannerDbContext is created.");
            var connectionString = WorkspaceConnections.ForSchema(
                settings.RuntimeConnectionString,
                workspace.SchemaName,
                settings.MaximumPoolSizePerWorkspace);
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(PlannerDbContext).Assembly.FullName));
        });

        services.AddScoped<IPlanningService, PlanningService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<ITopicService, TopicService>();

        services
            .AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: ["live"])
            .AddCheck<PlannerDatabaseHealthCheck>("postgresql", tags: ["ready"]);

        return services;
    }
}
