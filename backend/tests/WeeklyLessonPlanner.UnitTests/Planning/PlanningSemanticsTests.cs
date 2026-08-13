namespace WeeklyLessonPlanner.UnitTests.Planning;

/// <summary>
/// Remaining executable specifications confirmed in Phase 0. Implement these with the scheduling engine in Phase 4.
/// </summary>
public sealed class PlanningSemanticsTests
{
    private const string PhaseFour = "Scheduling engine is intentionally deferred to Phase 4.";

    [Fact(Skip = PhaseFour)]
    public void InsertShiftStopsAtFirstEligibleGap() { }

    [Fact(Skip = PhaseFour)]
    public void InsertWithoutShiftOverwritesAndReturnsDisplacedInstance() { }

    [Fact(Skip = PhaseFour)]
    public void DeleteWithShiftClosesOnlyTheNewGap() { }

    [Fact(Skip = PhaseFour)]
    public void DeleteWithoutShiftLeavesDayEmpty() { }

    [Fact(Skip = PhaseFour)]
    public void DragCallsNoBackendOperationUntilDrop() { }

    [Fact(Skip = PhaseFour)]
    public void DropCombinesCheckboxAwareDeleteAndInsertAtomically() { }

    [Fact(Skip = PhaseFour)]
    public void GlobalMarkerShiftsEveryAffectedCourseAtomically() { }

    [Fact(Skip = PhaseFour)]
    public void CourseExamShiftsOnlyItsCourse() { }

}
