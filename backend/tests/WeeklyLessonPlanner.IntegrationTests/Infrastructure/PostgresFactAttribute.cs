namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

[AttributeUsage(AttributeTargets.Method)]
internal sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")))
        {
            Skip = "Set TEST_POSTGRES_CONNECTION to run PostgreSQL provider probes.";
        }
    }
}

