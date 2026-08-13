namespace WeeklyLessonPlanner.Infrastructure.Persistence;

/// <summary>
/// A foundation record used to verify PostgreSQL mappings before the Phase 2 domain schema exists.
/// </summary>
public sealed class DatabaseMetadata
{
    public Guid Id { get; set; }

    public required string Key { get; set; }

    public required string Value { get; set; }

    public DateOnly RecordedOn { get; set; }

    public uint Version { get; private set; }
}

