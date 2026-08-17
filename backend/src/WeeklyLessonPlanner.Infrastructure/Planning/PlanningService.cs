using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using WeeklyLessonPlanner.Core.Calendar;
using WeeklyLessonPlanner.Core.Planning;
using WeeklyLessonPlanner.Core.Topics;
using WeeklyLessonPlanner.Infrastructure.Calendar;
using WeeklyLessonPlanner.Infrastructure.Persistence;

namespace WeeklyLessonPlanner.Infrastructure.Planning;

public sealed class PlanningService(PlannerDbContext dbContext) : IPlanningService
{
    public async Task<PlanningServiceStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var provider = dbContext.Database.ProviderName ?? "unknown";
        var available = await dbContext.Database.CanConnectAsync(cancellationToken);
        return new PlanningServiceStatus(nameof(PlanningService), provider, available);
    }

    public async Task<PlanningImpactDto> PlaceTopicAsync(
        PlaceTopicCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var instance = await dbContext.TopicInstances
            .Include(item => item.Topic)
            .Include(item => item.Assignment)
            .SingleOrDefaultAsync(item => item.Id == command.TopicInstanceId, cancellationToken)
            ?? throw new KeyNotFoundException("Topic instance not found.");

        if (instance.CourseId != command.CourseId)
        {
            throw new PlanningConflictException("The topic instance belongs to another course.");
        }

        if (instance.Assignment is not null)
        {
            throw new PlanningConflictException("Only an unplanned topic instance can be placed.");
        }

        var schedule = await LoadScheduleAsync(command.CourseId, new HashSet<DateOnly>(), cancellationToken: cancellationToken);
        var assignment = new TopicAssignment
        {
            Id = Guid.NewGuid(),
            TopicInstanceId = instance.Id,
            CourseId = instance.CourseId,
            TopicInstance = instance
        };
        schedule.Assignments[assignment.Id] = assignment;
        var displaced = new List<Guid>();
        ScheduleMutationEngine.Place(
            schedule.State,
            schedule.EligibleSlots,
            command.Date,
            assignment.Id,
            command.InsertShiftsSchedule,
            displaced);

        var impact = await PersistScheduleAsync(
            schedule,
            assignment.Id,
            null,
            displaced,
            cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return impact;
    }

    public async Task<PlanningImpactDto?> RemoveTopicAsync(
        RemoveTopicCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var selected = await dbContext.TopicAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.AssignmentId, cancellationToken);
        if (selected is null)
        {
            return null;
        }

        var schedule = await LoadScheduleAsync(selected.CourseId, new HashSet<DateOnly>(), cancellationToken: cancellationToken);
        ScheduleMutationEngine.Remove(
            schedule.State,
            schedule.EligibleSlots,
            selected.Date,
            command.DeleteShiftsSchedule);

        var impact = await PersistScheduleAsync(
            schedule,
            null,
            selected.Id,
            [selected.Id],
            cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return impact;
    }

    public async Task<PlanningImpactDto?> DragTopicAsync(
        DragTopicCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var selected = await dbContext.TopicAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.AssignmentId, cancellationToken);
        if (selected is null)
        {
            return null;
        }

        var schedule = await LoadScheduleAsync(selected.CourseId, new HashSet<DateOnly>(), cancellationToken: cancellationToken);
        if (selected.Date != command.DestinationDate)
        {
            var draggedId = ScheduleMutationEngine.Remove(
                schedule.State,
                schedule.EligibleSlots,
                selected.Date,
                command.DeleteShiftsSchedule);
            var displaced = new List<Guid>();
            ScheduleMutationEngine.Place(
                schedule.State,
                schedule.EligibleSlots,
                command.DestinationDate,
                draggedId,
                command.InsertShiftsSchedule,
                displaced);

            var impact = await PersistScheduleAsync(
                schedule,
                draggedId,
                draggedId,
                displaced,
                cancellationToken);
            await CommitConflictSafeAsync(transaction, cancellationToken);
            return impact;
        }

        var noOp = BuildImpact(schedule, selected.Id, selected.Id, []);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return noOp;
    }

    public async Task<GlobalDayMarkerDto> CreateGlobalMarkerAsync(
        SaveGlobalDayMarkerCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMarker(command);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await ValidateMarkerDatesAsync([command.Date], null, cancellationToken);
        await ShiftForGlobalFixedDatesAsync(new HashSet<DateOnly> { command.Date }, null, cancellationToken);

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

    public async Task<IReadOnlyList<GlobalDayMarkerDto>> CreateGlobalMarkerRangeAsync(
        SaveGlobalDayMarkerRangeCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMarker(new SaveGlobalDayMarkerCommand(command.From, command.Type, command.Label));
        if (command.Until < command.From)
        {
            throw new ArgumentException("Until must be on or after On/From.");
        }

        var dates = Enumerable.Range(0, command.Until.DayNumber - command.From.DayNumber + 1)
            .Select(command.From.AddDays)
            .ToArray();
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await ValidateMarkerDatesAsync(dates, null, cancellationToken);
        await ShiftForGlobalFixedDatesAsync(dates.ToHashSet(), null, cancellationToken);

        var markers = dates.Select(date => new GlobalDayMarker
        {
            Id = Guid.NewGuid(),
            Date = date,
            Type = command.Type,
            Label = NormalizeOptional(command.Label)
        }).ToArray();
        dbContext.GlobalDayMarkers.AddRange(markers);
        await SaveConflictSafeAsync("The marker range overlaps an existing holiday or event.", cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return markers.Select(CalendarService.ToDto).ToArray();
    }

    public async Task<GlobalDayMarkerDto?> UpdateGlobalMarkerAsync(
        Guid id,
        SaveGlobalDayMarkerCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMarker(command);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var marker = await dbContext.GlobalDayMarkers.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (marker is null)
        {
            return null;
        }

        await ValidateMarkerDatesAsync([command.Date], id, cancellationToken);
        if (marker.Date != command.Date)
        {
            await ShiftForGlobalFixedDatesAsync(new HashSet<DateOnly> { command.Date }, id, cancellationToken);
        }

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
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await ValidateExamDateAsync(command, null, cancellationToken);
        await ShiftForCourseFixedDateAsync(command.CourseId, command.Date, null, cancellationToken);

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
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var exam = await dbContext.CourseExams.SingleOrDefaultAsync(item => item.Id == id, cancellationToken);
        if (exam is null)
        {
            return null;
        }

        await ValidateExamDateAsync(command, id, cancellationToken);
        if (exam.CourseId != command.CourseId || exam.Date != command.Date)
        {
            await ShiftForCourseFixedDateAsync(command.CourseId, command.Date, id, cancellationToken);
        }

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

    public async Task<TopicInstanceDto?> CopyScheduledTopicAsync(
        Guid sourceInstanceId,
        CancellationToken cancellationToken = default)
    {
        var source = await dbContext.TopicInstances
            .Include(item => item.Topic)
            .Include(item => item.Assignment)
            .SingleOrDefaultAsync(item => item.Id == sourceInstanceId, cancellationToken);
        if (source is null)
        {
            return null;
        }

        if (source.Assignment is null)
        {
            throw new PlanningConflictException("Only a scheduled topic instance can be copied.");
        }

        var copy = new TopicInstance
        {
            Id = Guid.NewGuid(),
            TopicId = source.TopicId,
            CourseId = source.CourseId
        };
        dbContext.TopicInstances.Add(copy);
        await dbContext.SaveChangesAsync(cancellationToken);
        return ToTopicInstanceDto(copy, source.Topic);
    }

    private async Task ShiftForGlobalFixedDatesAsync(
        IReadOnlySet<DateOnly> blockedDates,
        Guid? ignoredMarkerId,
        CancellationToken cancellationToken)
    {
        var affectedCourses = await dbContext.TopicAssignments
            .Where(item => blockedDates.Contains(item.Date))
            .Select(item => item.CourseId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var schedules = new List<CourseSchedule>();

        foreach (var courseId in affectedCourses)
        {
            var schedule = await LoadScheduleAsync(
                courseId,
                blockedDates,
                ignoredMarkerId,
                cancellationToken: cancellationToken);
            ScheduleMutationEngine.ShiftBlockedAssignmentsForward(
                schedule.State,
                schedule.EligibleSlots,
                blockedDates);
            schedules.Add(schedule);
        }

        foreach (var schedule in schedules)
        {
            await PersistScheduleAsync(schedule, null, null, [], cancellationToken);
        }
    }

    private async Task ShiftForCourseFixedDateAsync(
        Guid courseId,
        DateOnly blockedDate,
        Guid? ignoredExamId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.TopicAssignments.AnyAsync(
                item => item.CourseId == courseId && item.Date == blockedDate,
                cancellationToken))
        {
            return;
        }

        HashSet<DateOnly> blockedDates = [blockedDate];
        var schedule = await LoadScheduleAsync(
            courseId,
            blockedDates,
            ignoredExamId: ignoredExamId,
            cancellationToken: cancellationToken);
        ScheduleMutationEngine.ShiftBlockedAssignmentsForward(
            schedule.State,
            schedule.EligibleSlots,
            blockedDates);
        await PersistScheduleAsync(schedule, null, null, [], cancellationToken);
    }

    private async Task<CourseSchedule> LoadScheduleAsync(
        Guid courseId,
        IReadOnlySet<DateOnly> additionalBlockedDates,
        Guid? ignoredMarkerId = null,
        Guid? ignoredExamId = null,
        CancellationToken cancellationToken = default)
    {
        var config = await dbContext.AppConfigs.SingleAsync(item => item.Id == AppConfig.SingletonId, cancellationToken);
        var weekdays = (await dbContext.CourseWeekdays
                .Where(item => item.CourseId == courseId)
                .Select(item => item.Weekday)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        if (weekdays.Count == 0)
        {
            if (!await dbContext.Courses.AnyAsync(item => item.Id == courseId, cancellationToken))
            {
                throw new KeyNotFoundException("Course not found.");
            }

            throw new PlanningConflictException("The course has no eligible teaching weekdays.");
        }

        var blockedDates = (await dbContext.GlobalDayMarkers
                .Where(item => item.Id != ignoredMarkerId)
                .Select(item => item.Date)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        blockedDates.UnionWith(await dbContext.CourseExams
            .Where(item => item.CourseId == courseId && item.Id != ignoredExamId)
            .Select(item => item.Date)
            .ToListAsync(cancellationToken));
        blockedDates.UnionWith(additionalBlockedDates);

        var eligibleSlots = Enumerable
            .Range(0, config.PlanningEnd.DayNumber - config.PlanningStart.DayNumber + 1)
            .Select(config.PlanningStart.AddDays)
            .Where(date => weekdays.Contains(ToIsoWeekday(date)) && !blockedDates.Contains(date))
            .ToArray();
        var assignments = await dbContext.TopicAssignments
            .Include(item => item.TopicInstance)
            .ThenInclude(item => item.Topic)
            .Where(item => item.CourseId == courseId)
            .ToListAsync(cancellationToken);

        return new CourseSchedule(
            courseId,
            eligibleSlots,
            assignments.ToDictionary(item => item.Date, item => item.Id),
            assignments.ToDictionary(item => item.Id),
            assignments.ToDictionary(item => item.Id, item => item.Date));
    }

    private async Task<PlanningImpactDto> PersistScheduleAsync(
        CourseSchedule schedule,
        Guid? insertedAssignmentId,
        Guid? removedAssignmentId,
        IReadOnlyCollection<Guid> explicitlyDisplacedIds,
        CancellationToken cancellationToken)
    {
        var finalDates = schedule.State.ToDictionary(item => item.Value, item => item.Key);
        var removed = schedule.OriginalDates.Keys
            .Where(id => !finalDates.ContainsKey(id))
            .Select(id => schedule.Assignments[id])
            .ToArray();
        var changed = schedule.OriginalDates
            .Where(item => finalDates.TryGetValue(item.Key, out var date) && date != item.Value)
            .Select(item => schedule.Assignments[item.Key])
            .ToArray();
        var added = finalDates.Keys
            .Where(id => !schedule.OriginalDates.ContainsKey(id))
            .Select(id => schedule.Assignments[id])
            .ToArray();

        dbContext.TopicAssignments.RemoveRange(removed);
        for (var index = 0; index < changed.Length; index++)
        {
            changed[index].Date = DateOnly.MinValue.AddDays(index);
        }

        if (removed.Length > 0 || changed.Length > 0)
        {
            await SaveConflictSafeAsync("The course schedule changed concurrently. Refresh it and try again.", cancellationToken);
        }

        foreach (var assignment in changed)
        {
            assignment.Date = finalDates[assignment.Id];
        }

        foreach (var assignment in added)
        {
            assignment.Date = finalDates[assignment.Id];
            dbContext.TopicAssignments.Add(assignment);
        }

        if (changed.Length > 0 || added.Length > 0)
        {
            await SaveConflictSafeAsync("The course schedule changed concurrently. Refresh it and try again.", cancellationToken);
        }

        return BuildImpact(schedule, insertedAssignmentId, removedAssignmentId, explicitlyDisplacedIds);
    }

    private static PlanningImpactDto BuildImpact(
        CourseSchedule schedule,
        Guid? insertedAssignmentId,
        Guid? removedAssignmentId,
        IReadOnlyCollection<Guid> explicitlyDisplacedIds)
    {
        var finalDates = schedule.State.ToDictionary(item => item.Value, item => item.Key);
        var moved = schedule.OriginalDates
            .Where(item => finalDates.TryGetValue(item.Key, out var date) && date != item.Value)
            .Select(item => new AssignmentMoveDto(
                item.Key,
                schedule.Assignments[item.Key].TopicInstanceId,
                item.Value,
                finalDates[item.Key]))
            .OrderBy(item => item.From)
            .ToArray();
        var unplannedIds = schedule.OriginalDates.Keys
            .Where(id => !finalDates.ContainsKey(id))
            .Concat(explicitlyDisplacedIds)
            .Distinct()
            .ToArray();
        var unplanned = unplannedIds
            .Where(schedule.Assignments.ContainsKey)
            .Select(id => schedule.Assignments[id].TopicInstance)
            .Select(item => ToTopicInstanceDto(item, item.Topic))
            .ToArray();
        var affectedDates = moved.SelectMany(item => new[] { item.From, item.To })
            .Concat(insertedAssignmentId.HasValue && finalDates.TryGetValue(insertedAssignmentId.Value, out var insertedDate)
                ? [insertedDate]
                : [])
            .Concat(removedAssignmentId.HasValue && schedule.OriginalDates.TryGetValue(removedAssignmentId.Value, out var removedDate)
                ? [removedDate]
                : [])
            .Distinct()
            .Order()
            .ToArray();

        return new PlanningImpactDto(
            insertedAssignmentId.HasValue && finalDates.TryGetValue(insertedAssignmentId.Value, out var finalInsertedDate)
                ? ToAssignmentDto(schedule.Assignments[insertedAssignmentId.Value], finalInsertedDate)
                : null,
            removedAssignmentId.HasValue && schedule.OriginalDates.TryGetValue(removedAssignmentId.Value, out var originalRemovedDate)
                ? ToAssignmentDto(schedule.Assignments[removedAssignmentId.Value], originalRemovedDate)
                : null,
            moved,
            affectedDates,
            unplanned);
    }

    private async Task ValidateMarkerDatesAsync(
        IReadOnlyCollection<DateOnly> dates,
        Guid? markerId,
        CancellationToken cancellationToken)
    {
        var config = await dbContext.AppConfigs.SingleAsync(item => item.Id == AppConfig.SingletonId, cancellationToken);
        if (dates.Any(date => date < config.PlanningStart || date > config.PlanningEnd))
        {
            throw new ArgumentException("The complete marker range must be inside the inclusive planning range.");
        }

        if (await dbContext.GlobalDayMarkers.AnyAsync(
                item => dates.Contains(item.Date) && item.Id != markerId,
                cancellationToken))
        {
            throw new PlanningConflictException("A holiday or event already exists in the selected date range.");
        }

        if (await dbContext.CourseExams.AnyAsync(item => dates.Contains(item.Date), cancellationToken))
        {
            throw new PlanningConflictException("A global marker cannot coexist with a course exam on this date.");
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
    }

    private async Task EnsureDateInRangeAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var config = await dbContext.AppConfigs.SingleAsync(item => item.Id == AppConfig.SingletonId, cancellationToken);
        if (date < config.PlanningStart || date > config.PlanningEnd)
        {
            throw new ArgumentException("The date must be inside the inclusive planning range.");
        }
    }

    private Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken) =>
        dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

    private async Task SaveConflictSafeAsync(string message, CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new PlanningConflictException("The calendar changed concurrently. Refresh it and try again.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new PlanningConflictException(message, exception);
        }
    }

    private static async Task CommitConflictSafeAsync(
        IDbContextTransaction transaction,
        CancellationToken cancellationToken)
    {
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (PostgresException exception) when (
            exception.SqlState is PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected)
        {
            throw new PlanningConflictException("The calendar changed concurrently. Refresh it and try again.", exception);
        }
    }

    private static AssignmentImpactDto ToAssignmentDto(TopicAssignment assignment, DateOnly date) => new(
        assignment.Id,
        assignment.TopicInstanceId,
        assignment.CourseId,
        date,
        assignment.TopicInstance.Topic.Heading,
        assignment.TopicInstance.Topic.Description);

    private static TopicInstanceDto ToTopicInstanceDto(TopicInstance instance, Topic topic) => new(
        instance.Id,
        instance.TopicId,
        instance.CourseId,
        topic.Heading,
        topic.Description);

    private static IsoWeekday ToIsoWeekday(DateOnly date) => date.DayOfWeek == DayOfWeek.Sunday
        ? IsoWeekday.Sunday
        : (IsoWeekday)date.DayOfWeek;

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

    private sealed record CourseSchedule(
        Guid CourseId,
        IReadOnlyList<DateOnly> EligibleSlots,
        Dictionary<DateOnly, Guid> State,
        Dictionary<Guid, TopicAssignment> Assignments,
        Dictionary<Guid, DateOnly> OriginalDates);
}
