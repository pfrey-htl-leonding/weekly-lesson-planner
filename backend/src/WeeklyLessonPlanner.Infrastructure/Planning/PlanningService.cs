using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.Infrastructure.Planning;

public sealed class PlanningService(PlannerDbContext dbContext) : IPlanningService
{
    public async Task<PlanningServiceStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var provider = dbContext.Database.ProviderName ?? "unknown";
        var available = await dbContext.Database.CanConnectAsync(cancellationToken);

        return new PlanningServiceStatus(nameof(PlanningService), provider, available);
    }

    public async Task<GlobalDayMarkerDto> CreateGlobalMarkerAsync(
        SaveGlobalDayMarkerCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMarker(command);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await ValidateMarkerDateAsync(command.Date, null, cancellationToken);

        var marker = new GlobalDayMarker
        {
            Id = Guid.NewGuid(),
            Date = command.Date,
            Type = command.Type,
            Label = NormalizeOptional(command.Label)
        };
        dbContext.GlobalDayMarkers.Add(marker);
        await SaveConflictSafeAsync("A holiday or event already exists on this date.", cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return CalendarService.ToDto(marker);
    }

    public async Task<GlobalDayMarkerDto?> UpdateGlobalMarkerAsync(
        Guid id,
        SaveGlobalDayMarkerCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMarker(command);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var marker = await dbContext.GlobalDayMarkers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (marker is null)
        {
            return null;
        }

        await ValidateMarkerDateAsync(command.Date, id, cancellationToken);
        marker.Date = command.Date;
        marker.Type = command.Type;
        marker.Label = NormalizeOptional(command.Label);
        await SaveConflictSafeAsync("A holiday or event already exists on this date.", cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return CalendarService.ToDto(marker);
    }

    public async Task<bool> DeleteGlobalMarkerAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var marker = await dbContext.GlobalDayMarkers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (marker is null)
        {
            return false;
        }

        dbContext.GlobalDayMarkers.Remove(marker);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<CourseExamDto> CreateCourseExamAsync(
        SaveCourseExamCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateExam(command);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await ValidateExamDateAsync(command, null, cancellationToken);

        var exam = new CourseExam
        {
            Id = Guid.NewGuid(),
            CourseId = command.CourseId,
            Date = command.Date,
            Name = command.Name.Trim()
        };
        dbContext.CourseExams.Add(exam);
        await SaveConflictSafeAsync("This course already has an exam on this date.", cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return CalendarService.ToDto(exam);
    }

    public async Task<CourseExamDto?> UpdateCourseExamAsync(
        Guid id,
        SaveCourseExamCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateExam(command);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var exam = await dbContext.CourseExams.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (exam is null)
        {
            return null;
        }

        await ValidateExamDateAsync(command, id, cancellationToken);
        exam.CourseId = command.CourseId;
        exam.Date = command.Date;
        exam.Name = command.Name.Trim();
        await SaveConflictSafeAsync("This course already has an exam on this date.", cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return CalendarService.ToDto(exam);
    }

    public async Task<bool> DeleteCourseExamAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var exam = await dbContext.CourseExams.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (exam is null)
        {
            return false;
        }

        dbContext.CourseExams.Remove(exam);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ValidateMarkerDateAsync(DateOnly date, Guid? markerId, CancellationToken cancellationToken)
    {
        await EnsureDateInRangeAsync(date, cancellationToken);
        if (await dbContext.GlobalDayMarkers.AnyAsync(item => item.Date == date && item.Id != markerId, cancellationToken))
        {
            throw new PlanningConflictException("A holiday or event already exists on this date.");
        }

        if (await dbContext.CourseExams.AnyAsync(item => item.Date == date, cancellationToken))
        {
            throw new PlanningConflictException("A global marker cannot coexist with a course exam on this date.");
        }

        if (await dbContext.TopicAssignments.AnyAsync(item => item.Date == date, cancellationToken))
        {
            throw new PlanningConflictException(
                "This date contains scheduled topics. Automatic marker shifting is introduced in Phase 4.");
        }
    }

    private async Task ValidateExamDateAsync(
        SaveCourseExamCommand command,
        Guid? examId,
        CancellationToken cancellationToken)
    {
        await EnsureDateInRangeAsync(command.Date, cancellationToken);
        if (!await dbContext.Courses.AnyAsync(item => item.Id == command.CourseId, cancellationToken))
        {
            throw new KeyNotFoundException("Course not found.");
        }

        if (await dbContext.GlobalDayMarkers.AnyAsync(item => item.Date == command.Date, cancellationToken))
        {
            throw new PlanningConflictException("An exam cannot coexist with a global marker on this date.");
        }

        if (await dbContext.CourseExams.AnyAsync(
                item => item.CourseId == command.CourseId && item.Date == command.Date && item.Id != examId,
                cancellationToken))
        {
            throw new PlanningConflictException("This course already has an exam on this date.");
        }

        if (await dbContext.TopicAssignments.AnyAsync(
                item => item.CourseId == command.CourseId && item.Date == command.Date,
                cancellationToken))
        {
            throw new PlanningConflictException(
                "This course/date contains a scheduled topic. Automatic exam shifting is introduced in Phase 4.");
        }
    }

    private async Task EnsureDateInRangeAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var config = await dbContext.AppConfigs.SingleAsync(item => item.Id == AppConfig.SingletonId, cancellationToken);
        if (date < config.PlanningStart || date > config.PlanningEnd)
        {
            throw new ArgumentException("The date must be inside the inclusive planning range.");
        }
    }

    private async Task SaveConflictSafeAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new PlanningConflictException(message, exception);
        }
    }

    private static async Task CommitConflictSafeAsync(
        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new PlanningConflictException(
                "The calendar changed concurrently. Refresh it and try again.",
                exception);
        }
    }

    private static void ValidateMarker(SaveGlobalDayMarkerCommand command)
    {
        if (command.Type is not (GlobalDayMarkerType.Holiday or GlobalDayMarkerType.Event))
        {
            throw new ArgumentException("Marker type must be Holiday or Event.");
        }

        if (command.Label?.Length > 200)
        {
            throw new ArgumentException("Marker label must not exceed 200 characters.");
        }
    }

    private static void ValidateExam(SaveCourseExamCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Name.Trim().Length > 200)
        {
            throw new ArgumentException("Exam name is required and must not exceed 200 characters.");
        }
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
