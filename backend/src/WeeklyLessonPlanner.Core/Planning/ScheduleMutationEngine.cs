namespace WeeklyLessonPlanner.Core.Planning;

/// <summary>
/// Applies the confirmed scheduling semantics to an in-memory course schedule.
/// Persistence and transaction handling remain the responsibility of IPlanningService.
/// </summary>
public static class ScheduleMutationEngine
{
    public static void Place(
        IDictionary<DateOnly, Guid> schedule,
        IReadOnlyList<DateOnly> eligibleSlots,
        DateOnly target,
        Guid assignmentId,
        bool shift,
        ICollection<Guid> displacedAssignments)
    {
        var targetIndex = IndexOfEligibleSlot(eligibleSlots, target);
        if (!schedule.TryGetValue(target, out var occupiedId))
        {
            schedule[target] = assignmentId;
            return;
        }

        if (!shift)
        {
            schedule[target] = assignmentId;
            displacedAssignments.Add(occupiedId);
            return;
        }

        var gapIndex = -1;
        for (var index = targetIndex + 1; index < eligibleSlots.Count; index++)
        {
            if (!schedule.ContainsKey(eligibleSlots[index]))
            {
                gapIndex = index;
                break;
            }
        }

        if (gapIndex < 0)
        {
            throw new PlanningConflictException("There is no later eligible free day for the shifted topic.");
        }

        for (var index = gapIndex; index > targetIndex; index--)
        {
            schedule[eligibleSlots[index]] = schedule[eligibleSlots[index - 1]];
        }

        schedule[target] = assignmentId;
    }

    public static Guid Remove(
        IDictionary<DateOnly, Guid> schedule,
        IReadOnlyList<DateOnly> eligibleSlots,
        DateOnly source,
        bool shift)
    {
        var sourceIndex = IndexOfEligibleSlot(eligibleSlots, source);
        if (!schedule.Remove(source, out var removedId))
        {
            throw new PlanningConflictException("The selected assignment is no longer scheduled.");
        }

        if (!shift)
        {
            return removedId;
        }

        for (var index = sourceIndex + 1; index < eligibleSlots.Count; index++)
        {
            var current = eligibleSlots[index];
            if (!schedule.Remove(current, out var movingId))
            {
                break;
            }

            schedule[eligibleSlots[index - 1]] = movingId;
        }

        return removedId;
    }

    public static void ShiftBlockedAssignmentsForward(
        IDictionary<DateOnly, Guid> schedule,
        IReadOnlyList<DateOnly> eligibleSlots,
        IReadOnlySet<DateOnly> newlyBlockedDates)
    {
        var blockedAssignments = schedule
            .Where(item => newlyBlockedDates.Contains(item.Key))
            .OrderByDescending(item => item.Key)
            .ToArray();

        foreach (var blocked in blockedAssignments)
        {
            schedule.Remove(blocked.Key);
            var movingId = blocked.Value;
            var placed = false;

            foreach (var slot in eligibleSlots.Where(slot => slot > blocked.Key))
            {
                if (!schedule.Remove(slot, out var occupiedId))
                {
                    schedule[slot] = movingId;
                    placed = true;
                    break;
                }

                schedule[slot] = movingId;
                movingId = occupiedId;
            }

            if (!placed)
            {
                throw new PlanningConflictException("There is not enough later course capacity for this fixed day.");
            }
        }
    }

    private static int IndexOfEligibleSlot(IReadOnlyList<DateOnly> eligibleSlots, DateOnly date)
    {
        for (var index = 0; index < eligibleSlots.Count; index++)
        {
            if (eligibleSlots[index] == date)
            {
                return index;
            }
        }

        throw new PlanningConflictException("The selected date is not an eligible lesson day for this course.");
    }
}
