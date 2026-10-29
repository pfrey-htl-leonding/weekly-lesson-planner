namespace WeeklyLessonPlanner.Infrastructure.Workspaces;

public enum WorkspaceStatus : short
{
    Provisioning = 1,
    Ready = 2,
    Failed = 3,
    Disabled = 4
}

public sealed record WorkspaceDescriptor(
    Guid Id,
    string Slug,
    string DisplayName,
    string SchemaName,
    WorkspaceStatus Status,
    string? ProvisioningError);

public sealed record WorkspaceSummary(
    Guid Id,
    string Slug,
    string DisplayName,
    string Status,
    string? ProvisioningError);

public sealed record CreateWorkspaceCommand(string DisplayName, string Slug);

public sealed class WorkspaceContextAccessor
{
    public WorkspaceDescriptor? Current { get; private set; }

    public void Set(WorkspaceDescriptor workspace)
    {
        if (Current is not null)
        {
            throw new InvalidOperationException("The request workspace has already been resolved.");
        }

        Current = workspace;
    }
}

public sealed record WorkspaceDatabaseSettings(
    string RuntimeConnectionString,
    string OwnerConnectionString,
    int MaximumCount,
    int MaximumPoolSizePerWorkspace,
    bool AllowCreate,
    string LegacyWorkspaceSlug,
    string LegacyWorkspaceDisplayName);

public sealed class WorkspaceValidationException(string message) : Exception(message);

public sealed class WorkspaceConflictException(string message) : Exception(message);
