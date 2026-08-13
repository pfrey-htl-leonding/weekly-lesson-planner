using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure;
using WeeklyLessonPlanner.Infrastructure.Persistence;
using WeeklyLessonPlanner.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);

if (builder.Environment.IsDevelopment())
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy => policy
            .WithOrigins(origins)
            .AllowAnyHeader()
            .AllowAnyMethod()));
}

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.UseCors();
    app.MapOpenApi();
}

if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<PlannerDbContext>();
    await dbContext.Database.MigrateAsync();
}

app.MapGet("/api/system/status", async (
        IPlanningService planningService,
        CancellationToken cancellationToken) =>
    {
        var status = await planningService.GetStatusAsync(cancellationToken);
        return TypedResults.Ok(status);
    })
    .WithName("GetSystemStatus")
    .WithTags("System");

app.MapCalendarEndpoints();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});

app.Run();

public partial class Program;
