import { TestBed } from '@angular/core/testing';
import { Observable, of, Subject } from 'rxjs';
import { App } from './app';
import { CalendarApi, CalendarView, EffectiveDayState, IsoWeekday } from './core/api/calendar-api';
import { SaveTopic, TopicApi, TopicDefinition } from './core/api/topic-api';

const calendarApi = {
  getConfig: () => of({
    planningStart: '2026-09-01', planningEnd: '2026-09-04',
    visibleWeekdays: [IsoWeekday.Monday, IsoWeekday.Tuesday, IsoWeekday.Wednesday, IsoWeekday.Thursday, IsoWeekday.Friday],
    holidayColor: '#008000', eventColor: '#0000ff', examColor: '#ffff00', weekNumbering: 'ISO 8601',
  }),
  getCourses: () => of([]),
  getMarkers: () => of([]),
  getExams: () => of([]),
  getCalendar: (): Observable<CalendarView> => of({
    planningStart: '2026-09-01', planningEnd: '2026-09-04', courseId: null,
    visibleWeekdays: [IsoWeekday.Monday, IsoWeekday.Tuesday, IsoWeekday.Wednesday, IsoWeekday.Thursday, IsoWeekday.Friday],
    weeks: [],
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
};

describe('App', () => {
  beforeEach(async () => {
    vi.clearAllMocks();
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        { provide: CalendarApi, useValue: calendarApi },
        { provide: TopicApi, useValue: topicApi },
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
      courseId: null,
      visibleWeekdays: [IsoWeekday.Monday],
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
});
