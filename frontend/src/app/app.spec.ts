import { TestBed } from '@angular/core/testing';
import { Observable, of, Subject } from 'rxjs';
import { App } from './app';
import { CalendarApi, CalendarView, EffectiveDayState, IsoWeekday } from './core/api/calendar-api';
import { SaveTopic, TopicApi, TopicDefinition } from './core/api/topic-api';
import { PlanningApi, PlanningImpact } from './core/api/planning-api';

const calendarApi = {
  getConfig: () => of({
    visibleWeekdays: [IsoWeekday.Monday, IsoWeekday.Tuesday, IsoWeekday.Wednesday, IsoWeekday.Thursday, IsoWeekday.Friday],
    holidayColor: '#008000', eventColor: '#0000ff', examColor: '#ffff00', weekNumbering: 'ISO 8601',
  }),
  getSchoolYears: () => of([{
    id: 'school-year', name: '2026/27', planningStart: '2026-09-01', planningEnd: '2027-06-30',
  }]),
  getCourses: () => of([]),
  getMarkers: () => of([]),
  getExams: () => of([]),
  getCalendar: (): Observable<CalendarView> => of({
    planningStart: '2026-09-01', planningEnd: '2026-09-04', courseId: null,
    schoolYearId: 'school-year', schoolYearName: '2026/27',
    visibleWeekdays: [IsoWeekday.Monday, IsoWeekday.Tuesday, IsoWeekday.Wednesday, IsoWeekday.Thursday, IsoWeekday.Friday],
    weeks: [],
    planningSummary: null,
  }),
};

const toTopic = (id: string, command: SaveTopic): TopicDefinition => ({
  id,
  ...command,
  totalInstanceCount: 1,
  plannedInstanceCount: 0,
  unplannedInstanceCount: 1,
});

const topicApi = {
  getTopics: () => of<TopicDefinition[]>([]),
  getUnplannedInstances: () => of([]),
  createTopic: vi.fn((command: SaveTopic) => of(toTopic('created', command))),
  updateTopic: vi.fn((id: string, command: SaveTopic) => of(toTopic(id, command))),
  copyScheduledInstance: vi.fn(() => of({
    id: 'copy', topicId: 'topic', courseId: 'course', heading: 'Trees', description: '',
  })),
};

const emptyImpact: PlanningImpact = {
  insertedAssignment: null,
  removedAssignment: null,
  movedAssignments: [],
  affectedDates: [],
  becameUnplanned: [],
};

const planningApi = {
  place: vi.fn(() => of(emptyImpact)),
  remove: vi.fn(() => of(emptyImpact)),
  drag: vi.fn(() => of(emptyImpact)),
  addAll: vi.fn(() => of({
    affectedTopicCount: 2,
    firstAffectedDate: '2026-09-07',
    lastAffectedDate: '2026-09-14',
  })),
  removeAll: vi.fn(() => of({
    affectedTopicCount: 3,
    firstAffectedDate: '2026-09-07',
    lastAffectedDate: '2026-09-21',
  })),
  moveExam: vi.fn(() => of({
    exam: {
      id: 'exam',
      courseId: 'course',
      date: '2026-09-14',
      name: 'Written exam',
    },
    swappedTopic: {
      assignmentId: 'assignment',
      topicInstanceId: 'instance',
      from: '2026-09-14',
      to: '2026-09-07',
    },
  })),
  rollOverCourse: vi.fn(() => of({
    course: {
      id: 'rolled-over-course',
      schoolYearId: 'target-year',
      name: 'Course',
      description: '',
      weekdays: [IsoWeekday.Friday],
    },
    topicDefinitionCount: 2,
    topicInstanceCount: 3,
    assignmentCount: 2,
    firstAssignedDate: '2027-09-03',
    lastAssignedDate: '2027-09-17',
    skippedFixedDates: ['2027-09-10'],
  })),
};

const eligibleDay = {
  date: '2026-09-07',
  weekday: IsoWeekday.Monday,
  isInPlanningRange: true,
  isCourseDay: true,
  state: EffectiveDayState.Normal,
  label: null,
  scheduledTopics: [],
};

const scheduledTopic = {
  assignmentId: 'assignment',
  topicInstanceId: 'instance',
  courseId: 'course',
  courseName: 'Course',
  heading: 'Trees',
  description: '',
};

describe('App', () => {
  beforeEach(async () => {
    vi.clearAllMocks();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        { provide: CalendarApi, useValue: calendarApi },
        { provide: TopicApi, useValue: topicApi },
        { provide: PlanningApi, useValue: planningApi },
      ],
    }).compileComponents();
  });

  it('creates the application shell', () => {
    const fixture = TestBed.createComponent(App);
    expect(fixture.componentInstance).toBeTruthy();
  });

  it('renders the planner title', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Weekly Lesson Planner');
  });

  it('shows the selected course planning summary above the unplanned topics', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.componentInstance.calendar = {
      ...fixture.componentInstance.calendar!,
      courseId: 'course',
      planningSummary: {
        lessonDayCount: 27,
        plannedTopicCount: 10,
        unplannedTopicCount: 17,
      },
    };
    fixture.detectChanges();
    await fixture.whenStable();

    const summary = fixture.nativeElement.querySelector('.topic-planning-summary');
    expect(summary.textContent).toContain('Lesson days:27');
    expect(summary.textContent).toContain('Planned:10');
    expect(summary.textContent).toContain('Unplanned:17');
  });

  it('filters unplanned topic instances by heading or description', () => {
    const fixture = TestBed.createComponent(App);
    fixture.componentInstance.unplannedTopics = [
      { id: '1', topicId: 'a', courseId: 'c', heading: 'Trees', description: 'Binary search trees' },
      { id: '2', topicId: 'b', courseId: 'c', heading: 'Sorting', description: 'Quicksort' },
    ];

    fixture.componentInstance.topicSearch = 'binary';

    expect(fixture.componentInstance.visibleUnplannedTopics().map(topic => topic.id)).toEqual(['1']);
  });

  it('renders date-only calendar values without a timezone day shift', () => {
    const fixture = TestBed.createComponent(App);

    expect(fixture.componentInstance.calendarDateLabel('2026-08-03')).toBe('3 Aug');
  });

  it('renders the calendar when its asynchronous response arrives without a user event', async () => {
    const calendarResponse = new Subject<CalendarView>();
    vi.spyOn(calendarApi, 'getCalendar').mockReturnValueOnce(calendarResponse);
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    calendarResponse.next({
      planningStart: '2026-08-03',
      planningEnd: '2026-08-03',
      schoolYearId: 'school-year',
      schoolYearName: '2026/27',
      courseId: null,
      visibleWeekdays: [IsoWeekday.Monday],
      planningSummary: null,
      weeks: [{
        isoYear: 2026,
        isoWeek: 32,
        days: [{
          date: '2026-08-03',
          weekday: IsoWeekday.Monday,
          isInPlanningRange: true,
          isCourseDay: false,
          state: EffectiveDayState.Normal,
          label: null,
          scheduledTopics: [],
        }],
      }],
    });
    calendarResponse.complete();
    await fixture.whenStable();

    expect(fixture.nativeElement.textContent).toContain('3 Aug');
  });

  it('updates an existing topic by case-insensitive name during import', () => {
    const fixture = TestBed.createComponent(App);
    fixture.componentInstance.selectedCourseId = 'course';
    fixture.componentInstance.topics = [toTopic('existing', {
      courseId: 'course',
      heading: 'Trees',
      description: 'Old description',
    })];
    fixture.componentInstance.dataTransferKind = 'topics';
    fixture.componentInstance.dataTransferText = 'trees;Updated description';

    fixture.componentInstance.importData();

    expect(topicApi.updateTopic).toHaveBeenCalledWith('existing', {
      courseId: 'course',
      heading: 'trees',
      description: 'Updated description',
    });
    expect(topicApi.createTopic).not.toHaveBeenCalled();
  });

  it('reloads the calendar after editing a shared topic definition', () => {
    const getCalendar = vi.spyOn(calendarApi, 'getCalendar');
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    component.editingTopicId = 'topic';
    component.topicDraft = { heading: 'Updated heading', description: 'Updated description' };

    component.saveTopic();

    expect(topicApi.updateTopic).toHaveBeenCalledWith('topic', {
      courseId: 'course',
      heading: 'Updated heading',
      description: 'Updated description',
    });
    expect(getCalendar).toHaveBeenCalledWith('course', undefined);
  });

  it('does not call the planning API while a topic is merely dragged over a valid day', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    const dragData = component.scheduledDragData(scheduledTopic, '2026-09-01');

    expect(component.canEnterDay(
      { data: dragData } as never,
      { data: eligibleDay } as never,
    )).toBe(true);
    expect(planningApi.place).not.toHaveBeenCalled();
    expect(planningApi.remove).not.toHaveBeenCalled();
    expect(planningApi.drag).not.toHaveBeenCalled();
  });

  it('places an unplanned topic exactly once on drop with the insertion option', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    const instance = { id: 'instance', topicId: 'topic', courseId: 'course', heading: 'Trees', description: '' };
    component.selectedCourseId = 'course';
    component.insertShiftsSchedule = true;

    component.onDayDrop({
      item: { data: component.unplannedDragData(instance) },
    } as never, eligibleDay);

    expect(planningApi.place).toHaveBeenCalledTimes(1);
    expect(planningApi.place).toHaveBeenCalledWith({
      topicInstanceId: 'instance',
      courseId: 'course',
      date: '2026-09-07',
      insertShiftsSchedule: true,
    });
  });

  it('sends one atomic drag command on drop with both checkbox values', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    component.insertShiftsSchedule = true;
    component.deleteShiftsSchedule = true;

    component.onDayDrop({
      item: { data: component.scheduledDragData(scheduledTopic, '2026-09-01') },
    } as never, eligibleDay);

    expect(planningApi.drag).toHaveBeenCalledTimes(1);
    expect(planningApi.drag).toHaveBeenCalledWith({
      assignmentId: 'assignment',
      destinationDate: '2026-09-07',
      deleteShiftsSchedule: true,
      insertShiftsSchedule: true,
    });
  });

  it('sends no command for an invalid or same-day drop', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    const data = component.scheduledDragData(scheduledTopic, eligibleDay.date);

    component.onDayDrop({ item: { data } } as never, {
      ...eligibleDay,
      state: EffectiveDayState.Holiday,
    });
    component.onDayDrop({ item: { data } } as never, eligibleDay);

    expect(planningApi.place).not.toHaveBeenCalled();
    expect(planningApi.remove).not.toHaveBeenCalled();
    expect(planningApi.drag).not.toHaveBeenCalled();
  });

  it('removes a scheduled topic dropped into the topic list using the deletion option', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    component.deleteShiftsSchedule = true;

    component.onTopicListDrop({
      item: { data: component.scheduledDragData(scheduledTopic, '2026-09-01') },
    } as never);

    expect(planningApi.remove).toHaveBeenCalledTimes(1);
    expect(planningApi.remove).toHaveBeenCalledWith({
      assignmentId: 'assignment',
      deleteShiftsSchedule: true,
    });
  });

  it('sends bounded add-all and remove-all commands for the selected course', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    component.multiplePlanningFrom = '2026-09-07';
    component.multiplePlanningUntil = '2026-10-05';

    component.addAllTopics();

    const command = {
      courseId: 'course',
      from: '2026-09-07',
      until: '2026-10-05',
    };
    expect(planningApi.addAll).toHaveBeenCalledWith(command);

    component.multiplePlanningFrom = command.from;
    component.multiplePlanningUntil = command.until;
    component.removeAllTopics();
    expect(planningApi.removeAll).toHaveBeenCalledWith(command);
  });

  it('moves an exam by one lesson day and updates its active edit date', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    const exam = {
      id: 'exam',
      courseId: 'course',
      date: '2026-09-07',
      name: 'Written exam',
    };
    component.selectedCourseId = 'course';
    component.editExam(exam);

    component.moveExam(exam, 1);

    expect(planningApi.moveExam).toHaveBeenCalledWith('exam', 1);
    expect(component.examDraft.date).toBe('2026-09-14');
    expect(component.message).toContain('swapped its scheduled topic');
  });

  it('renders both exam movement arrows on the calendar exam card', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    await fixture.whenStable();
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    component.exams = [{
      id: 'exam',
      courseId: 'course',
      date: '2026-09-07',
      name: 'Written exam',
    }];
    component.calendar = {
      planningStart: '2026-09-07',
      planningEnd: '2026-09-07',
      schoolYearId: 'school-year',
      schoolYearName: '2026/27',
      courseId: 'course',
      visibleWeekdays: [IsoWeekday.Monday],
      planningSummary: {
        lessonDayCount: 0,
        plannedTopicCount: 0,
        unplannedTopicCount: 0,
      },
      weeks: [{
        isoYear: 2026,
        isoWeek: 37,
        days: [{
          ...eligibleDay,
          state: EffectiveDayState.Exam,
          label: 'Written exam',
        }],
      }],
    };
    fixture.detectChanges();

    const buttons = fixture.nativeElement.querySelectorAll('.exam-card button');
    expect(buttons).toHaveLength(2);
    expect(buttons[0].getAttribute('aria-label')).toContain('previous lesson day');
    expect(buttons[1].getAttribute('aria-label')).toContain('next lesson day');
  });

  it('shifts forward while preserving the source gap regardless of checkbox values', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    component.deleteShiftsSchedule = true;
    component.insertShiftsSchedule = false;
    component.calendar = {
      planningStart: '2026-09-07',
      planningEnd: '2026-09-21',
      schoolYearId: 'school-year',
      schoolYearName: '2026/27',
      courseId: 'course',
      visibleWeekdays: [IsoWeekday.Monday],
      planningSummary: {
        lessonDayCount: 3,
        plannedTopicCount: 0,
        unplannedTopicCount: 0,
      },
      weeks: [
        { isoYear: 2026, isoWeek: 37, days: [{ ...eligibleDay, date: '2026-09-07' }] },
        { isoYear: 2026, isoWeek: 38, days: [{ ...eligibleDay, date: '2026-09-14' }] },
        { isoYear: 2026, isoWeek: 39, days: [{ ...eligibleDay, date: '2026-09-21' }] },
      ],
    };

    component.moveScheduled(scheduledTopic, '2026-09-07', 1);

    expect(planningApi.drag).toHaveBeenCalledTimes(1);
    expect(planningApi.drag).toHaveBeenCalledWith({
      assignmentId: 'assignment',
      destinationDate: '2026-09-14',
      deleteShiftsSchedule: false,
      insertShiftsSchedule: true,
    });
  });

  it('returns the course view to All topics when switching school year', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.selectedCourseId = 'course';
    component.selectedSchoolYearId = 'next-school-year';

    component.changeSchoolYearView();

    expect(component.selectedCourseId).toBe('');
  });

  it('defaults the rollover start date when its target school year changes', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.schoolYears = [
      { id: 'source-year', name: '2026/27', planningStart: '2026-09-01', planningEnd: '2027-06-30' },
      { id: 'target-year', name: '2027/28', planningStart: '2027-09-01', planningEnd: '2028-06-30' },
    ];
    component.rolloverDraft.targetSchoolYearId = 'target-year';
    component.rolloverDraft.targetStartDate = '2027-10-01';

    component.changeRolloverTargetYear();

    expect(component.rolloverDraft.targetStartDate).toBe('2027-09-01');
  });

  it('submits course rollover with an independently selected target lesson day', () => {
    const fixture = TestBed.createComponent(App);
    const component = fixture.componentInstance;
    component.rolloverDraft = {
      sourceCourseId: 'source-course',
      targetSchoolYearId: 'target-year',
      targetStartDate: '2027-09-01',
      targetWeekday: IsoWeekday.Friday,
    };

    component.rollOverCourse();

    expect(planningApi.rollOverCourse).toHaveBeenCalledWith({
      sourceCourseId: 'source-course',
      targetSchoolYearId: 'target-year',
      targetStartDate: '2027-09-01',
      targetWeekday: IsoWeekday.Friday,
    });
  });
});
