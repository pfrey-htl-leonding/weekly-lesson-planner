import { TestBed } from '@angular/core/testing';
import { of } from 'rxjs';
import { App } from './app';
import { CalendarApi, IsoWeekday } from './core/api/calendar-api';

const calendarApi = {
  getConfig: () => of({
    planningStart: '2026-09-01', planningEnd: '2026-09-04',
    visibleWeekdays: [IsoWeekday.Monday, IsoWeekday.Tuesday, IsoWeekday.Wednesday, IsoWeekday.Thursday, IsoWeekday.Friday],
    holidayColor: '#008000', eventColor: '#0000ff', examColor: '#ffff00', weekNumbering: 'ISO 8601',
  }),
  getCourses: () => of([]),
  getMarkers: () => of([]),
  getExams: () => of([]),
  getCalendar: () => of({
    planningStart: '2026-09-01', planningEnd: '2026-09-04', courseId: null,
    visibleWeekdays: [IsoWeekday.Monday, IsoWeekday.Tuesday, IsoWeekday.Wednesday, IsoWeekday.Thursday, IsoWeekday.Friday],
    weeks: [],
  }),
};

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [{ provide: CalendarApi, useValue: calendarApi }],
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
});
