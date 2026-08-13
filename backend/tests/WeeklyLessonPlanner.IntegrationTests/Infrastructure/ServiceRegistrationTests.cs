using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Infrastructure;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void InfrastructureResolvesScopedPlanningServiceAndDbContext()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PlannerDatabase"] =
                    "Host=localhost;Database=planner;Username=planner;Password=not-used"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PlannerDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPlanningService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICalendarService>());
    }
}
