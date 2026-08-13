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

namespace WeeklyLessonPlanner.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("PlannerDatabase");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "ConnectionStrings:PlannerDatabase must be configured.");
        }

        services.AddDbContext<PlannerDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(typeof(PlannerDbContext).Assembly.FullName)));

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
