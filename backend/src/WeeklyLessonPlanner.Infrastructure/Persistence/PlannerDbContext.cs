using Microsoft.EntityFrameworkCore;

namespace WeeklyLessonPlanner.Infrastructure.Persistence;

public sealed class PlannerDbContext(DbContextOptions<PlannerDbContext> options) : DbContext(options)
{
    public DbSet<DatabaseMetadata> DatabaseMetadata => Set<DatabaseMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var metadata = modelBuilder.Entity<DatabaseMetadata>();

        metadata.ToTable("database_metadata");
        metadata.HasKey(item => item.Id);
        metadata.HasIndex(item => item.Key).IsUnique();
        metadata.Property(item => item.Key).HasMaxLength(100);
        metadata.Property(item => item.Value).HasMaxLength(500);
        metadata.Property(item => item.RecordedOn).HasColumnType("date");
        metadata.Property(item => item.Version).IsRowVersion();
    }
}

