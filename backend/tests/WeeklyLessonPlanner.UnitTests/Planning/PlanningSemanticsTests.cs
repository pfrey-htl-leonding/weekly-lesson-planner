using WeeklyLessonPlanner.Core.Planning;

namespace WeeklyLessonPlanner.UnitTests.Planning;

public sealed class PlanningSemanticsTests
{
    private static readonly DateOnly First = new(2026, 9, 7);
    private static readonly DateOnly Second = new(2026, 9, 14);
    private static readonly DateOnly Third = new(2026, 9, 21);
    private static readonly DateOnly Fourth = new(2026, 9, 28);
    private static readonly DateOnly Fifth = new(2026, 10, 5);
    private static readonly DateOnly[] Slots = [First, Second, Third, Fourth, Fifth];

    [Fact]
    public void InsertShiftStopsAtFirstEligibleGap()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        var inserted = Guid.NewGuid();
        Dictionary<DateOnly, Guid> schedule = new()
        {
            [First] = a,
            [Second] = b,
            [Fourth] = c
        };

        ScheduleMutationEngine.Place(schedule, Slots, First, inserted, true, []);

        Assert.Equal(inserted, schedule[First]);
        Assert.Equal(a, schedule[Second]);
        Assert.Equal(b, schedule[Third]);
        Assert.Equal(c, schedule[Fourth]);
    }

    [Fact]
    public void InsertWithoutShiftOverwritesAndReturnsDisplacedInstance()
    {
        var displaced = Guid.NewGuid();
        var inserted = Guid.NewGuid();
        Dictionary<DateOnly, Guid> schedule = new() { [Second] = displaced };
        List<Guid> returned = [];

        ScheduleMutationEngine.Place(schedule, Slots, Second, inserted, false, returned);

        Assert.Equal(inserted, schedule[Second]);
        Assert.Equal([displaced], returned);
    }

    [Fact]
    public void InsertWithNoLaterCapacityRollsBackWhenCallerUsesWorkingCopy()
    {
        var original = Slots.ToDictionary(date => date, _ => Guid.NewGuid());
        var working = new Dictionary<DateOnly, Guid>(original);

        Assert.Throws<PlanningConflictException>(() =>
            ScheduleMutationEngine.Place(working, Slots, Second, Guid.NewGuid(), true, []));

        Assert.All(Slots, date => Assert.Equal(original[date], working[date]));
    }

    [Fact]
    public void DeleteWithShiftClosesOnlyTheNewGap()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        Dictionary<DateOnly, Guid> schedule = new()
        {
            [First] = a,
            [Second] = b,
            [Third] = c,
            [Fifth] = Guid.NewGuid()
        };

        Assert.Equal(a, ScheduleMutationEngine.Remove(schedule, Slots, First, true));

        Assert.Equal(b, schedule[First]);
        Assert.Equal(c, schedule[Second]);
        Assert.False(schedule.ContainsKey(Third));
        Assert.True(schedule.ContainsKey(Fifth));
    }

    [Fact]
    public void DeleteWithoutShiftLeavesDayEmpty()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        Dictionary<DateOnly, Guid> schedule = new() { [First] = a, [Second] = b };

        ScheduleMutationEngine.Remove(schedule, Slots, First, false);

        Assert.False(schedule.ContainsKey(First));
        Assert.Equal(b, schedule[Second]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DropCombinesCheckboxAwareDeleteAndInsertAtomically(bool deleteShifts, bool insertShifts)
    {
        var dragged = Guid.NewGuid();
        Dictionary<DateOnly, Guid> schedule = new()
        {
            [First] = dragged,
            [Second] = Guid.NewGuid(),
            [Third] = Guid.NewGuid()
        };

        var removed = ScheduleMutationEngine.Remove(schedule, Slots, First, deleteShifts);
        ScheduleMutationEngine.Place(schedule, Slots, Third, removed, insertShifts, []);

        Assert.Equal(dragged, schedule[Third]);
        Assert.Single(schedule.Values, id => id == dragged);
    }

    [Fact]
    public void MultiDayGlobalMarkerPreservesOrderAndSkipsAllBlockedDates()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var c = Guid.NewGuid();
        Dictionary<DateOnly, Guid> schedule = new()
        {
            [First] = a,
            [Second] = b,
            [Third] = c
        };
        HashSet<DateOnly> blocked = [First, Second];
        var eligible = Slots.Where(date => !blocked.Contains(date)).ToArray();

        ScheduleMutationEngine.ShiftBlockedAssignmentsForward(schedule, eligible, blocked);

        Assert.Equal(a, schedule[Third]);
        Assert.Equal(b, schedule[Fourth]);
        Assert.Equal(c, schedule[Fifth]);
        Assert.DoesNotContain(schedule.Keys, blocked.Contains);
    }

    [Fact]
    public void FixedDayWithNoCapacityIsRejected()
    {
        var a = Guid.NewGuid();
        Dictionary<DateOnly, Guid> schedule = new() { [Fifth] = a };

        Assert.Throws<PlanningConflictException>(() =>
            ScheduleMutationEngine.ShiftBlockedAssignmentsForward(schedule, Slots[..^1], new HashSet<DateOnly> { Fifth }));
    }
}
