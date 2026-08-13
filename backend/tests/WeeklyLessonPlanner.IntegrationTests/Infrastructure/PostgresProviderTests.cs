using Microsoft.EntityFrameworkCore;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.IntegrationTests.Infrastructure;

public sealed class PostgresProviderTests
{
    [PostgresFact]
    public async Task DateOnlyAndTransactionRollbackRoundTrip()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var key = $"transaction-{Guid.NewGuid():N}";
        var expectedDate = new DateOnly(2026, 8, 13);

        await using (var transaction = await dbContext.Database.BeginTransactionAsync())
        {
            dbContext.DatabaseMetadata.Add(NewRecord(key, expectedDate));
            await dbContext.SaveChangesAsync();

            var storedDate = await dbContext.DatabaseMetadata
                .Where(item => item.Key == key)
                .Select(item => item.RecordedOn)
                .SingleAsync();
            Assert.Equal(expectedDate, storedDate);

            await transaction.RollbackAsync();
        }

        dbContext.ChangeTracker.Clear();
        Assert.False(await dbContext.DatabaseMetadata.AnyAsync(item => item.Key == key));
    }

    [PostgresFact]
    public async Task UniqueConstraintIsEnforced()
    {
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        var key = $"unique-{Guid.NewGuid():N}";
        dbContext.DatabaseMetadata.Add(NewRecord(key, new DateOnly(2026, 8, 13)));
        await dbContext.SaveChangesAsync();

        try
        {
            dbContext.DatabaseMetadata.Add(NewRecord(key, new DateOnly(2026, 8, 14)));
            await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        }
        finally
        {
            dbContext.ChangeTracker.Clear();
            var persisted = await dbContext.DatabaseMetadata.SingleAsync(item => item.Key == key);
            dbContext.DatabaseMetadata.Remove(persisted);
            await dbContext.SaveChangesAsync();
        }
    }

    [PostgresFact]
    public async Task XminRejectsStaleUpdates()
    {
        var key = $"concurrency-{Guid.NewGuid():N}";
        await using var firstContext = CreateDbContext();
        await firstContext.Database.MigrateAsync();
        var record = NewRecord(key, new DateOnly(2026, 8, 13));
        firstContext.DatabaseMetadata.Add(record);
        await firstContext.SaveChangesAsync();

        try
        {
            await using var secondContext = CreateDbContext();
            var competingRecord = await secondContext.DatabaseMetadata.SingleAsync(item => item.Key == key);
            competingRecord.Value = "updated by another context";
            await secondContext.SaveChangesAsync();

            record.Value = "stale update";
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => firstContext.SaveChangesAsync());
        }
        finally
        {
            firstContext.ChangeTracker.Clear();
            var persisted = await firstContext.DatabaseMetadata.SingleAsync(item => item.Key == key);
            firstContext.DatabaseMetadata.Remove(persisted);
            await firstContext.SaveChangesAsync();
        }
    }

    private static PlannerDbContext CreateDbContext()
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_POSTGRES_CONNECTION")!;
        var options = new DbContextOptionsBuilder<PlannerDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new PlannerDbContext(options);
    }

    private static DatabaseMetadata NewRecord(string key, DateOnly date) => new()
    {
        Id = Guid.NewGuid(),
        Key = key,
        Value = "provider probe",
        RecordedOn = date
    };
}
