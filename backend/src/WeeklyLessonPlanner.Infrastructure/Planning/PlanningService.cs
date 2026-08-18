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

    public async Task<MultipleTopicPlanningResultDto> AddAllTopicsAsync(
        MultipleTopicPlanningCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var (from, until) = await ResolvePlanningIntervalAsync(command, cancellationToken);
        var schedule = await LoadScheduleAsync(
            command.CourseId,
            new HashSet<DateOnly>(),
            cancellationToken: cancellationToken);
        var freeDates = schedule.EligibleSlots
            .Where(date => date >= from && date <= until && !schedule.State.ContainsKey(date))
            .ToArray();
        var instances = await dbContext.TopicInstances
            .Include(item => item.Topic)
            .Include(item => item.Assignment)
            .Where(item => item.CourseId == command.CourseId && item.Assignment == null)
            .OrderBy(item => item.Topic.Heading.ToLower())
            .ThenBy(item => item.Topic.Heading)
            .ThenBy(item => item.Id)
            .Take(freeDates.Length)
            .ToListAsync(cancellationToken);

        for (var index = 0; index < instances.Count; index++)
        {
            var assignment = new TopicAssignment
            {
                Id = Guid.NewGuid(),
                TopicInstanceId = instances[index].Id,
                CourseId = command.CourseId,
                TopicInstance = instances[index]
            };
            schedule.Assignments.Add(assignment.Id, assignment);
            schedule.State.Add(freeDates[index], assignment.Id);
        }

        await PersistScheduleAsync(schedule, null, null, [], cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return new MultipleTopicPlanningResultDto(
            instances.Count,
            instances.Count > 0 ? freeDates[0] : null,
            instances.Count > 0 ? freeDates[instances.Count - 1] : null);
    }

    public async Task<MultipleTopicPlanningResultDto> RemoveAllTopicsAsync(
        MultipleTopicPlanningCommand command,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var (from, until) = await ResolvePlanningIntervalAsync(command, cancellationToken);
        var assignments = await dbContext.TopicAssignments
            .Where(item => item.CourseId == command.CourseId && item.Date >= from && item.Date <= until)
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);

        dbContext.TopicAssignments.RemoveRange(assignments);
        if (assignments.Count > 0)
        {
            await SaveConflictSafeAsync(
                "The course schedule changed concurrently. Refresh it and try again.",
                cancellationToken);
        }
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return new MultipleTopicPlanningResultDto(
            assignments.Count,
            assignments.Count > 0 ? assignments[0].Date : null,
            assignments.Count > 0 ? assignments[^1].Date : null);
    }

    public async Task<CourseRolloverResultDto> RollOverCourseAsync(
        CourseRolloverCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(command.TargetWeekday))
        {
            throw new ArgumentException("Target lesson weekday must be between Monday and Sunday.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var source = await dbContext.Courses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(item => item.Topics)
            .ThenInclude(item => item.Instances)
            .ThenInclude(item => item.Assignment)
            .SingleOrDefaultAsync(item => item.Id == command.SourceCourseId, cancellationToken)
            ?? throw new KeyNotFoundException("Source course not found.");
        var targetSchoolYear = await dbContext.SchoolYears
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == command.TargetSchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException("Target school year not found.");

        EnsureDateInRange(command.TargetStartDate, targetSchoolYear);
        if (await dbContext.Courses.AnyAsync(
                item => item.SchoolYearId == targetSchoolYear.Id && item.Name == source.Name,
                cancellationToken))
        {
            throw new PlanningConflictException(
                $"A course named '{source.Name}' already exists in the target school year.");
        }

        var blockedDates = (await dbContext.GlobalDayMarkers
                .AsNoTracking()
                .Where(item => item.SchoolYearId == targetSchoolYear.Id)
                .Select(item => item.Date)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        var eligibleSlots = Enumerable
            .Range(0, targetSchoolYear.PlanningEnd.DayNumber - command.TargetStartDate.DayNumber + 1)
            .Select(command.TargetStartDate.AddDays)
            .Where(date => ToIsoWeekday(date) == command.TargetWeekday && !blockedDates.Contains(date))
            .ToArray();
        var scheduledSourceInstances = source.Topics
            .SelectMany(item => item.Instances)
            .Where(item => item.Assignment is not null)
            .OrderBy(item => item.Assignment!.Date)
            .ToArray();
        var assignmentCount = Math.Min(eligibleSlots.Length, scheduledSourceInstances.Length);

        var targetCourse = new Course
        {
            Id = Guid.NewGuid(),
            SchoolYearId = targetSchoolYear.Id,
            Name = source.Name,
            Description = source.Description,
            Weekdays =
            [
                new CourseWeekday
                {
                    Weekday = command.TargetWeekday
                }
            ]
        };
        var copiedInstances = new Dictionary<Guid, TopicInstance>();
        foreach (var sourceTopic in source.Topics)
        {
            var targetTopic = new Topic
            {
                Id = Guid.NewGuid(),
                CourseId = targetCourse.Id,
                Heading = sourceTopic.Heading,
                Description = sourceTopic.Description,
                Course = targetCourse
            };
            targetCourse.Topics.Add(targetTopic);

            foreach (var sourceInstance in sourceTopic.Instances)
            {
                var targetInstance = new TopicInstance
                {
                    Id = Guid.NewGuid(),
                    TopicId = targetTopic.Id,
                    CourseId = targetCourse.Id,
                    Topic = targetTopic
                };
                targetTopic.Instances.Add(targetInstance);
                copiedInstances.Add(sourceInstance.Id, targetInstance);
            }
        }

        for (var index = 0; index < assignmentCount; index++)
        {
            var targetInstance = copiedInstances[scheduledSourceInstances[index].Id];
            targetInstance.Assignment = new TopicAssignment
            {
                Id = Guid.NewGuid(),
                TopicInstanceId = targetInstance.Id,
                CourseId = targetCourse.Id,
                Date = eligibleSlots[index],
                TopicInstance = targetInstance
            };
        }

        dbContext.Courses.Add(targetCourse);
        await SaveConflictSafeAsync(
            "The target course could not be created because its data conflicts with an existing course.",
            cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);

        var lastAssignedDate = assignmentCount > 0
            ? eligibleSlots[assignmentCount - 1]
            : (DateOnly?)null;
        var skippedThrough = scheduledSourceInstances.Length > assignmentCount
            ? targetSchoolYear.PlanningEnd
            : lastAssignedDate;
        var skippedFixedDates = skippedThrough.HasValue && scheduledSourceInstances.Length > 0
            ? blockedDates
                .Where(date => date >= command.TargetStartDate &&
                    date <= skippedThrough.Value &&
                    ToIsoWeekday(date) == command.TargetWeekday)
                .Order()
                .ToArray()
            : [];
        return new CourseRolloverResultDto(
            new CourseDto(
                targetCourse.Id,
                targetCourse.SchoolYearId,
                targetCourse.Name,
                targetCourse.Description,
                [command.TargetWeekday]),
            source.Topics.Count,
            copiedInstances.Count,
            assignmentCount,
            assignmentCount > 0 ? eligibleSlots[0] : null,
            lastAssignedDate,
            skippedFixedDates);
    }

    public async Task<GlobalDayMarkerDto> CreateGlobalMarkerAsync(
        SaveGlobalDayMarkerCommand command,
        CancellationToken cancellationToken = default)
    {
        ValidateMarker(command);
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await ValidateMarkerDatesAsync(command.SchoolYearId, [command.Date], null, cancellationToken);
        await ShiftForGlobalFixedDatesAsync(command.SchoolYearId, new HashSet<DateOnly> { command.Date }, null, cancellationToken);

        var marker = new GlobalDayMarker
        {
            Id = Guid.NewGuid(),
            SchoolYearId = command.SchoolYearId,
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
        ValidateMarker(new SaveGlobalDayMarkerCommand(command.SchoolYearId, command.From, command.Type, command.Label));
        if (command.Until < command.From)
        {
            throw new ArgumentException("Until must be on or after On/From.");
        }

        var dates = Enumerable.Range(0, command.Until.DayNumber - command.From.DayNumber + 1)
            .Select(command.From.AddDays)
            .ToArray();
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        await ValidateMarkerDatesAsync(command.SchoolYearId, dates, null, cancellationToken);
        await ShiftForGlobalFixedDatesAsync(command.SchoolYearId, dates.ToHashSet(), null, cancellationToken);

        var markers = dates.Select(date => new GlobalDayMarker
        {
            Id = Guid.NewGuid(),
            SchoolYearId = command.SchoolYearId,
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

        if (marker.SchoolYearId != command.SchoolYearId)
        {
            throw new PlanningConflictException("A marker cannot be moved to another school year.");
        }

        await ValidateMarkerDatesAsync(command.SchoolYearId, [command.Date], id, cancellationToken);
        if (marker.Date != command.Date)
        {
            await ShiftForGlobalFixedDatesAsync(command.SchoolYearId, new HashSet<DateOnly> { command.Date }, id, cancellationToken);
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

    public async Task<MoveCourseExamResultDto> MoveCourseExamAsync(
        MoveCourseExamCommand command,
        CancellationToken cancellationToken = default)
    {
        if (command.Direction is not (-1 or 1))
        {
            throw new ArgumentException("Exam movement direction must be -1 or 1.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        var exam = await dbContext.CourseExams
            .Include(item => item.Course)
            .ThenInclude(item => item.SchoolYear)
            .SingleOrDefaultAsync(item => item.Id == command.ExamId, cancellationToken)
            ?? throw new KeyNotFoundException("Course exam not found.");
        var weekdays = (await dbContext.CourseWeekdays
                .Where(item => item.CourseId == exam.CourseId)
                .Select(item => item.Weekday)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        if (!weekdays.Contains(ToIsoWeekday(exam.Date)))
        {
            throw new PlanningConflictException(
                "This exam is not on a teaching weekday and cannot be moved with the lesson-day arrows.");
        }

        var blockedDates = (await dbContext.GlobalDayMarkers.AsNoTracking()
                .Where(item => item.SchoolYearId == exam.Course.SchoolYearId)
                .Select(item => item.Date)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        blockedDates.UnionWith(await dbContext.CourseExams.AsNoTracking()
            .Where(item => item.CourseId == exam.CourseId && item.Id != exam.Id)
            .Select(item => item.Date)
            .ToListAsync(cancellationToken));
        var lessonDates = Enumerable
            .Range(
                0,
                exam.Course.SchoolYear.PlanningEnd.DayNumber -
                    exam.Course.SchoolYear.PlanningStart.DayNumber + 1)
            .Select(exam.Course.SchoolYear.PlanningStart.AddDays)
            .Where(date => weekdays.Contains(ToIsoWeekday(date)) && !blockedDates.Contains(date))
            .ToArray();
        var sourceIndex = Array.IndexOf(lessonDates, exam.Date);
        var targetIndex = sourceIndex + command.Direction;
        if (sourceIndex < 0 || targetIndex < 0 || targetIndex >= lessonDates.Length)
        {
            throw new PlanningConflictException(
                $"There is no {(command.Direction < 0 ? "earlier" : "later")} lesson day available for this exam.");
        }

        var sourceDate = exam.Date;
        var targetDate = lessonDates[targetIndex];
        if (await dbContext.TopicAssignments.AnyAsync(
                item => item.CourseId == exam.CourseId && item.Date == sourceDate,
                cancellationToken))
        {
            throw new PlanningConflictException("The exam date unexpectedly contains a scheduled topic.");
        }
        var swappedAssignment = await dbContext.TopicAssignments
            .SingleOrDefaultAsync(
                item => item.CourseId == exam.CourseId && item.Date == targetDate,
                cancellationToken);

        exam.Date = targetDate;
        if (swappedAssignment is not null)
        {
            swappedAssignment.Date = sourceDate;
        }
        await SaveConflictSafeAsync(
            "The exam could not be moved because the course schedule changed concurrently.",
            cancellationToken);
        await CommitConflictSafeAsync(transaction, cancellationToken);
        return new MoveCourseExamResultDto(
            CalendarService.ToDto(exam),
            swappedAssignment is null
                ? null
                : new AssignmentMoveDto(
                    swappedAssignment.Id,
                    swappedAssignment.TopicInstanceId,
                    targetDate,
                    sourceDate));
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
        Guid schoolYearId,
        IReadOnlySet<DateOnly> blockedDates,
        Guid? ignoredMarkerId,
        CancellationToken cancellationToken)
    {
        var affectedCourses = await dbContext.TopicAssignments
            .Where(item => item.TopicInstance.Topic.Course.SchoolYearId == schoolYearId && blockedDates.Contains(item.Date))
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
        var course = await dbContext.Courses.AsNoTracking()
            .Include(item => item.SchoolYear)
            .SingleOrDefaultAsync(item => item.Id == courseId, cancellationToken)
            ?? throw new KeyNotFoundException("Course not found.");
        var weekdays = (await dbContext.CourseWeekdays
                .Where(item => item.CourseId == courseId)
                .Select(item => item.Weekday)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        if (weekdays.Count == 0)
        {
            throw new PlanningConflictException("The course has no eligible teaching weekdays.");
        }

        var blockedDates = (await dbContext.GlobalDayMarkers
                .Where(item => item.SchoolYearId == course.SchoolYearId && item.Id != ignoredMarkerId)
                .Select(item => item.Date)
                .ToListAsync(cancellationToken))
            .ToHashSet();
        blockedDates.UnionWith(await dbContext.CourseExams
            .Where(item => item.CourseId == courseId && item.Id != ignoredExamId)
            .Select(item => item.Date)
            .ToListAsync(cancellationToken));
        blockedDates.UnionWith(additionalBlockedDates);

        var eligibleSlots = Enumerable
            .Range(0, course.SchoolYear.PlanningEnd.DayNumber - course.SchoolYear.PlanningStart.DayNumber + 1)
            .Select(course.SchoolYear.PlanningStart.AddDays)
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

    private async Task<(DateOnly From, DateOnly Until)> ResolvePlanningIntervalAsync(
        MultipleTopicPlanningCommand command,
        CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses.AsNoTracking()
            .Include(item => item.SchoolYear)
            .SingleOrDefaultAsync(item => item.Id == command.CourseId, cancellationToken)
            ?? throw new KeyNotFoundException("Course not found.");
        var from = command.From ?? course.SchoolYear.PlanningStart;
        var until = command.Until ?? course.SchoolYear.PlanningEnd;
        EnsureDateInRange(from, course.SchoolYear);
        EnsureDateInRange(until, course.SchoolYear);
        if (until < from)
        {
            throw new ArgumentException("Until must be on or after From.");
        }

        return (from, until);
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
        Guid schoolYearId,
        IReadOnlyCollection<DateOnly> dates,
        Guid? markerId,
        CancellationToken cancellationToken)
    {
        var schoolYear = await dbContext.SchoolYears.SingleOrDefaultAsync(item => item.Id == schoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException("School year not found.");
        if (dates.Any(date => date < schoolYear.PlanningStart || date > schoolYear.PlanningEnd))
        {
            throw new ArgumentException("The complete marker range must be inside the inclusive planning range.");
        }

        if (await dbContext.GlobalDayMarkers.AnyAsync(
                item => item.SchoolYearId == schoolYearId && dates.Contains(item.Date) && item.Id != markerId,
                cancellationToken))
        {
            throw new PlanningConflictException("A holiday or event already exists in the selected date range.");
        }

        if (await dbContext.CourseExams.AnyAsync(
                item => item.Course.SchoolYearId == schoolYearId && dates.Contains(item.Date), cancellationToken))
        {
            throw new PlanningConflictException("A global marker cannot coexist with a course exam on this date.");
        }
    }

    private async Task ValidateExamDateAsync(
        SaveCourseExamCommand command,
        Guid? examId,
        CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses.AsNoTracking().Include(item => item.SchoolYear)
            .SingleOrDefaultAsync(item => item.Id == command.CourseId, cancellationToken)
            ?? throw new KeyNotFoundException("Course not found.");
        EnsureDateInRange(command.Date, course.SchoolYear);

        if (await dbContext.GlobalDayMarkers.AnyAsync(
                item => item.SchoolYearId == course.SchoolYearId && item.Date == command.Date, cancellationToken))
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

    private static void EnsureDateInRange(DateOnly date, SchoolYear schoolYear)
    {
        if (date < schoolYear.PlanningStart || date > schoolYear.PlanningEnd)
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
