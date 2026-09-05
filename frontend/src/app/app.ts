import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { CdkDrag, CdkDragDrop, CdkDropList, DragDropModule } from '@angular/cdk/drag-drop';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelect, MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatToolbarModule } from '@angular/material/toolbar';
import { forkJoin, map, Observable, of } from 'rxjs';
import {
  AppConfig,
  CalendarApi,
  CalendarDay,
  CalendarView,
  Course,
  CourseExam,
  EffectiveDayState,
  GlobalDayMarker,
  GlobalDayMarkerType,
  IsoWeekday,
  SaveCourse,
  SaveSchoolYear,
  SchoolYear,
  ScheduledTopic,
} from './core/api/calendar-api';
import { SaveTopic, TopicApi, TopicDefinition, TopicInstance } from './core/api/topic-api';
import {
  CourseRolloverCommand,
  MultipleTopicPlanningResult,
  PlanningApi,
  PlanningImpact,
} from './core/api/planning-api';
import {
  parseNameDescriptionCsv,
  writeNameDescriptionCsv,
} from './core/data/name-description-csv';

type PlannerDragData =
  | { kind: 'unplanned'; courseId: string; instance: TopicInstance }
  | { kind: 'scheduled'; courseId: string; topic: ScheduledTopic; sourceDate: string };

@Component({
  selector: 'app-root',
  imports: [
    CommonModule,
    DragDropModule,
    FormsModule,
    MatButtonModule,
    MatCardModule,
    MatCheckboxModule,
    MatFormFieldModule,
    MatIconModule,
    MatInputModule,
    MatSelectModule,
    MatRadioModule,
    MatSnackBarModule,
    MatTabsModule,
    MatToolbarModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly api = inject(CalendarApi);
  private readonly topicApi = inject(TopicApi);
  private readonly planningApi = inject(PlanningApi);
  private readonly snackBar = inject(MatSnackBar);
  private readonly changeDetector = inject(ChangeDetectorRef);

  readonly weekdays = Object.values(IsoWeekday).filter((value): value is IsoWeekday => typeof value === 'number');
  readonly markerTypes = GlobalDayMarkerType;
  readonly states = EffectiveDayState;
  readonly allCoursesOptionValue = '__all_courses__';

  config: AppConfig | null = null;
  schoolYears: SchoolYear[] = [];
  courses: Course[] = [];
  markers: GlobalDayMarker[] = [];
  exams: CourseExam[] = [];
  calendar: CalendarView | null = null;
  courseCalendars: Record<string, CalendarView> = {};
  topics: TopicDefinition[] = [];
  unplannedTopics: TopicInstance[] = [];
  managementTabIndex = 0;
  selectedCourseIds: string[] = [];
  topicCourseId = '';
  selectedSchoolYearId = '';
  schoolYearDraft: SaveSchoolYear = { name: '', planningStart: '', planningEnd: '' };
  editingSchoolYearId: string | null = null;
  courseDraft: SaveCourse = { schoolYearId: '', name: '', description: '', weekdays: [] };
  editingCourseId: string | null = null;
  rolloverDraft: CourseRolloverCommand = {
    sourceCourseId: '',
    targetSchoolYearId: '',
    targetStartDate: '',
    targetWeekday: IsoWeekday.Monday,
  };
  markerDraft = { date: '', until: '', type: GlobalDayMarkerType.Holiday, label: '' };
  editingMarkerId: string | null = null;
  examDraft = { date: '', name: '' };
  editingExamId: string | null = null;
  editingExamCourseId = '';
  topicDraft: Omit<SaveTopic, 'courseId'> = { heading: '', description: '' };
  editingTopicId: string | null = null;
  topicSearch = '';
  placementDate = '';
  multiplePlanningFrom = '';
  multiplePlanningUntil = '';
  editShiftsSchedule = false;
  dataTransferText = '';
  dataTransferKind: 'topics' | 'courses' = 'topics';
  busy = false;
  message = '';
  error = '';

  readonly canEnterDay = (
    drag: CdkDrag<PlannerDragData>,
    drop: CdkDropList<CalendarDay>,
  ): boolean => this.canDropOnDay(drop.data, drag.data.courseId);

  /** The legacy single-course value is also the course used by single-course-only forms. */
  get selectedCourseId(): string {
    return this.selectedCourseIds.length === 1 ? this.selectedCourseIds[0] : '';
  }

  set selectedCourseId(courseId: string) {
    this.selectedCourseIds = courseId ? [courseId] : [];
    this.topicCourseId = courseId;
  }

  ngOnInit(): void {
    this.reloadAll();
  }

  reloadAll(): void {
    this.busy = true;
    forkJoin({
      config: this.api.getConfig(),
      schoolYears: this.api.getSchoolYears(),
      courses: this.api.getCourses(),
    }).subscribe({
      next: ({ config, schoolYears, courses }) => {
        this.config = config;
        this.schoolYears = schoolYears;
        this.courses = courses;
        if (!this.selectedSchoolYearId || !schoolYears.some(item => item.id === this.selectedSchoolYearId)) {
          this.selectedSchoolYearId = schoolYears[0]?.id ?? '';
        }
        this.selectedCourseIds = this.selectedCourseIds.filter(id => courses.some(course => course.id === id));
        if (this.selectedCourseIds.length > 0) {
          this.selectedSchoolYearId = courses.find(course => course.id === this.selectedCourseIds[0])?.schoolYearId ?? this.selectedSchoolYearId;
          this.selectedCourseIds = this.selectedCourseIds.filter(id =>
            courses.find(course => course.id === id)?.schoolYearId === this.selectedSchoolYearId);
        }
        this.syncTopicCourse();
        this.courseDraft.schoolYearId ||= this.selectedSchoolYearId;
        this.syncRolloverOptions();
        this.changeDetector.markForCheck();
        this.reloadCalendar();
      },
      error: error => this.handleError(error),
    });
  }

  reloadCalendar(): void {
    this.busy = true;
    const courseIds = [...this.selectedCourseIds];
    const calendarRequests = courseIds.length > 0
      ? courseIds.map(courseId => this.api.getCalendar(courseId, undefined))
      : [this.api.getCalendar(undefined, this.selectedSchoolYearId || undefined)];
    forkJoin({
      calendars: forkJoin(calendarRequests),
      markers: this.selectedSchoolYearId ? this.api.getMarkers(this.selectedSchoolYearId) : of([]),
      exams: this.combineCourseRequests(courseIds.map(courseId => this.api.getExams(courseId))),
      topics: this.combineCourseRequests(courseIds.map(courseId => this.topicApi.getTopics(courseId))),
      unplannedTopics: this.combineCourseRequests(
        courseIds.map(courseId => this.topicApi.getUnplannedInstances(courseId))),
    }).subscribe({
      next: ({ calendars, markers, exams, topics, unplannedTopics }) => {
        this.courseCalendars = Object.fromEntries(
          calendars.filter(calendar => calendar.courseId).map(calendar => [calendar.courseId!, calendar]),
        );
        const calendar = courseIds.length > 0 ? this.mergeCourseCalendars(calendars) : calendars[0];
        this.calendar = calendar;
        this.markers = markers;
        this.exams = exams;
        this.topics = this.sortTopics(topics);
        this.unplannedTopics = this.sortTopics(unplannedTopics);
        if (courseIds.length > 0) {
          if (!this.multiplePlanningFrom ||
              this.multiplePlanningFrom < calendar.planningStart ||
              this.multiplePlanningFrom > calendar.planningEnd) {
            this.multiplePlanningFrom = calendar.planningStart;
          }
          if (this.multiplePlanningUntil &&
              (this.multiplePlanningUntil < calendar.planningStart ||
               this.multiplePlanningUntil > calendar.planningEnd)) {
            this.multiplePlanningUntil = '';
          }
        } else {
          this.multiplePlanningFrom = '';
          this.multiplePlanningUntil = '';
        }
        this.busy = false;
        this.error = '';
        this.changeDetector.markForCheck();
      },
      error: error => this.handleError(error),
    });
  }

  changeCourseView(): void {
    const firstCourseId = this.selectedCourseIds[0];
    if (firstCourseId) {
      this.selectedSchoolYearId = this.courses.find(course => course.id === firstCourseId)?.schoolYearId ?? this.selectedSchoolYearId;
      if (this.selectedCourseIds.length === 1) this.rolloverDraft.sourceCourseId = firstCourseId;
    }
    this.syncTopicCourse();
    this.clearExam();
    this.clearTopic();
    this.reloadCalendar();
  }

  changeCourseSelection(courseIds: string[], select: MatSelect): void {
    const allCoursesSelected = courseIds.includes(this.allCoursesOptionValue);
    this.selectedCourseIds = allCoursesSelected ? [] : courseIds;
    this.changeCourseView();
    if (allCoursesSelected) select.close();
  }

  selectOnlyCourse(courseId: string, select: MatSelect, event: MouseEvent): void {
    event.stopPropagation();
    this.selectedCourseIds = [courseId];
    this.changeCourseView();
    select.close();
  }

  saveConfig(): void {
    if (!this.config) return;
    this.busy = true;
    this.api.updateConfig({
      visibleWeekdays: this.config.visibleWeekdays,
      holidayColor: this.config.holidayColor,
      eventColor: this.config.eventColor,
      examColor: this.config.examColor,
    }).subscribe({
      next: config => {
        this.config = config;
        this.succeed('Configuration saved.');
        this.reloadCalendar();
      },
      error: error => this.handleError(error),
    });
  }

  changeSchoolYearView(): void {
    this.selectedCourseIds = [];
    this.topicCourseId = '';
    this.courseDraft.schoolYearId = this.selectedSchoolYearId;
    this.clearExam();
    this.clearTopic();
    this.clearMarker();
    this.syncRolloverOptions();
    this.reloadCalendar();
  }

  selectSchoolYear(schoolYear: SchoolYear | null): void {
    if (!schoolYear) {
      this.editingSchoolYearId = null;
      this.schoolYearDraft = { name: '', planningStart: '', planningEnd: '' };
      return;
    }
    this.editingSchoolYearId = schoolYear.id;
    this.schoolYearDraft = {
      name: schoolYear.name,
      planningStart: schoolYear.planningStart,
      planningEnd: schoolYear.planningEnd,
    };
  }

  saveSchoolYear(): void {
    const request = this.editingSchoolYearId
      ? this.api.updateSchoolYear(this.editingSchoolYearId, this.schoolYearDraft)
      : this.api.createSchoolYear(this.schoolYearDraft);
    this.busy = true;
    request.subscribe({
      next: schoolYear => {
        this.selectedSchoolYearId = schoolYear.id;
        this.selectSchoolYear(null);
        this.succeed(`School year “${schoolYear.name}” saved.`);
        this.reloadAll();
      },
      error: error => this.handleError(error),
    });
  }

  deleteSchoolYear(schoolYear: SchoolYear): void {
    if (!window.confirm(`Delete school year “${schoolYear.name}” and all its courses and planning data?`)) return;
    this.api.deleteSchoolYear(schoolYear.id).subscribe({
      next: () => {
        if (this.selectedSchoolYearId === schoolYear.id) {
          this.selectedSchoolYearId = '';
          this.selectedCourseId = '';
        }
        this.succeed('School year deleted.');
        this.reloadAll();
      },
      error: error => this.handleError(error),
    });
  }

  selectCourse(course: Course | null): void {
    if (!course) {
      this.editingCourseId = null;
      this.courseDraft = { schoolYearId: this.selectedSchoolYearId, name: '', description: '', weekdays: [] };
      return;
    }
    this.editingCourseId = course.id;
    this.courseDraft = { schoolYearId: course.schoolYearId, name: course.name, description: course.description, weekdays: [...course.weekdays] };
  }

  saveCourse(): void {
    this.courseDraft.schoolYearId ||= this.selectedSchoolYearId;
    const wasEditing = this.editingCourseId !== null;
    const request = this.editingCourseId
      ? this.api.updateCourse(this.editingCourseId, this.courseDraft)
      : this.api.createCourse(this.courseDraft);
    this.busy = true;
    request.subscribe({
      next: course => {
        this.courses = [
          ...this.courses.filter(item => item.id !== course.id),
          course,
        ].sort((left, right) => left.name.localeCompare(right.name));
        this.selectedCourseId = course.id;
        this.selectCourse(null);
        this.succeed(wasEditing
          ? `Course “${course.name}” updated.`
          : `Course “${course.name}” added and selected.`);
        this.reloadAll();
      },
      error: error => this.handleError(error),
    });
  }

  deleteCourse(course: Course): void {
    if (!window.confirm(`Delete course “${course.name}” and its calendar data?`)) return;
    this.api.deleteCourse(course.id).subscribe({
      next: () => {
        this.selectedCourseIds = this.selectedCourseIds.filter(id => id !== course.id);
        this.syncTopicCourse();
        this.succeed('Course deleted.');
        this.reloadAll();
      },
      error: error => this.handleError(error),
    });
  }

  rolloverSourceCourses(): Course[] {
    return this.coursesForSelectedSchoolYear();
  }

  rolloverTargetSchoolYears(): SchoolYear[] {
    return this.schoolYears.filter(schoolYear => schoolYear.id !== this.selectedSchoolYearId);
  }

  changeRolloverTargetYear(): void {
    const target = this.schoolYears.find(item => item.id === this.rolloverDraft.targetSchoolYearId);
    this.rolloverDraft.targetStartDate = target?.planningStart ?? '';
  }

  rollOverCourse(): void {
    if (!this.rolloverDraft.sourceCourseId ||
        !this.rolloverDraft.targetSchoolYearId ||
        !this.rolloverDraft.targetStartDate) {
      this.fail('Choose a source course, target school year, start date, and lesson day.');
      return;
    }

    this.busy = true;
    this.planningApi.rollOverCourse({ ...this.rolloverDraft }).subscribe({
      next: result => {
        this.selectedSchoolYearId = result.course.schoolYearId;
        this.selectedCourseId = result.course.id;
        const range = result.firstAssignedDate && result.lastAssignedDate
          ? ` from ${result.firstAssignedDate} to ${result.lastAssignedDate}`
          : '';
        const skipped = result.skippedFixedDates.length > 0
          ? `; skipped ${result.skippedFixedDates.length} fixed lesson day(s)`
          : '';
        this.succeed(
          `Rolled over “${result.course.name}”: ${result.assignmentCount} scheduled and ` +
          `${result.topicInstanceCount - result.assignmentCount} unplanned topic instance(s)${range}${skipped}.`,
        );
        this.reloadAll();
      },
      error: error => this.handleError(error),
    });
  }

  editMarker(marker: GlobalDayMarker): void {
    this.editingMarkerId = marker.id;
    this.markerDraft = { date: marker.date, until: '', type: marker.type, label: marker.label ?? '' };
  }

  clearMarker(): void {
    this.editingMarkerId = null;
    this.markerDraft = { date: '', until: '', type: GlobalDayMarkerType.Holiday, label: '' };
  }

  saveMarker(): void {
    const command = {
      schoolYearId: this.selectedSchoolYearId,
      date: this.markerDraft.date,
      type: this.markerDraft.type,
      label: this.markerDraft.label || null,
    };
    const request = this.editingMarkerId
      ? this.api.updateMarker(this.editingMarkerId, command)
      : this.api.createMarker(command);
    this.busy = true;
    request.subscribe({
      next: () => {
        this.clearMarker();
        this.succeed('Global day marker saved.');
        this.reloadAll();
      },
      error: error => this.handleError(error),
    });
  }

  saveMarkerRange(): void {
    if (!this.markerDraft.date || !this.markerDraft.until) return;
    this.busy = true;
    this.api.createMarkerRange({
      schoolYearId: this.selectedSchoolYearId,
      from: this.markerDraft.date,
      until: this.markerDraft.until,
      type: this.markerDraft.type,
      label: this.markerDraft.label || null,
    }).subscribe({
      next: markers => {
        this.clearMarker();
        this.succeed(`${markers.length} day markers added.`);
        this.reloadAll();
      },
      error: error => this.handleError(error),
    });
  }

  deleteMarker(marker: GlobalDayMarker): void {
    this.api.deleteMarker(marker.id).subscribe({
      next: () => { this.succeed('Marker deleted.'); this.reloadAll(); },
      error: error => this.handleError(error),
    });
  }

  editExam(exam: CourseExam): void {
    this.editingExamId = exam.id;
    this.editingExamCourseId = exam.courseId;
    this.examDraft = { date: exam.date, name: exam.name };
  }

  clearExam(): void {
    this.editingExamId = null;
    this.editingExamCourseId = '';
    this.examDraft = { date: '', name: '' };
  }

  saveExam(): void {
    const courseId = this.editingExamCourseId || this.selectedCourseId;
    if (!courseId) return;
    const command = { courseId, ...this.examDraft };
    const request = this.editingExamId
      ? this.api.updateExam(this.editingExamId, command)
      : this.api.createExam(command);
    request.subscribe({
      next: () => {
        this.clearExam();
        this.succeed('Course exam saved.');
        this.reloadAll();
      },
      error: error => this.handleError(error),
    });
  }

  moveExam(exam: CourseExam, direction: -1 | 1): void {
    this.busy = true;
    this.planningApi.moveExam(exam.id, direction).subscribe({
      next: result => {
        if (this.editingExamId === exam.id) {
          this.examDraft.date = result.exam.date;
        }
        this.succeed(
          `Moved exam “${exam.name}”${result.swappedTopic ? ' and swapped its scheduled topic' : ''}.`,
        );
        this.reloadCalendar();
      },
      error: error => this.handleError(error),
    });
  }

  examOnDate(date: string): CourseExam | null {
    return this.exams.find(exam => exam.date === date) ?? null;
  }

  deleteExam(exam: CourseExam): void {
    this.api.deleteExam(exam.id).subscribe({
      next: () => { this.succeed('Exam deleted.'); this.reloadAll(); },
      error: error => this.handleError(error),
    });
  }

  editTopic(topic: TopicDefinition | TopicInstance | ScheduledTopic): void {
    this.editingTopicId = 'topicId' in topic ? topic.topicId : topic.id;
    this.topicCourseId = topic.courseId;
    this.topicDraft = { heading: topic.heading, description: topic.description };
    this.managementTabIndex = 0;
    this.changeDetector.markForCheck();
  }

  clearTopic(): void {
    this.editingTopicId = null;
    this.topicDraft = { heading: '', description: '' };
    this.syncTopicCourse();
  }

  saveTopic(): void {
    if (!this.topicCourseId || !this.isCourseSelected(this.topicCourseId)) return;
    const command: SaveTopic = { courseId: this.topicCourseId, ...this.topicDraft };
    const wasEditing = this.editingTopicId !== null;
    const request = this.editingTopicId
      ? this.topicApi.updateTopic(this.editingTopicId, command)
      : this.topicApi.createTopic(command);
    this.busy = true;
    request.subscribe({
      next: topic => {
        this.clearTopic();
        this.topicSearch = '';
        this.succeed(wasEditing
          ? `Topic “${topic.heading}” updated.`
          : `Topic “${topic.heading}” added to the unplanned list.`);
        this.reloadCalendar();
      },
      error: error => this.handleError(error),
    });
  }

  deleteTopic(topic: TopicDefinition): void {
    if (!window.confirm(`Delete topic definition “${topic.heading}” and all its unplanned instances?`)) return;
    this.topicApi.deleteTopic(topic.id).subscribe({
      next: () => { this.succeed('Topic definition deleted.'); this.reloadCalendar(); },
      error: error => this.handleError(error),
    });
  }

  deleteTopicInstance(instance: TopicInstance): void {
    this.topicApi.deleteUnplannedInstance(instance.id).subscribe({
      next: () => { this.succeed('One unplanned topic instance deleted.'); this.reloadCalendar(); },
      error: error => this.handleError(error),
    });
  }

  visibleUnplannedTopics(): TopicInstance[] {
    const search = this.topicSearch.trim().toLocaleLowerCase();
    if (!search) return this.unplannedTopics;
    return this.unplannedTopics.filter(topic =>
      topic.heading.toLocaleLowerCase().includes(search) ||
      topic.description.toLocaleLowerCase().includes(search));
  }

  coursesForSelectedSchoolYear(): Course[] {
    return this.courses.filter(course => course.schoolYearId === this.selectedSchoolYearId);
  }

  courseName(courseId: string): string {
    return this.courses.find(course => course.id === courseId)?.name ?? 'Unknown course';
  }

  isCourseSelected(courseId: string): boolean {
    return this.selectedCourseIds.includes(courseId);
  }

  selectedSchoolYear(): SchoolYear | null {
    return this.schoolYears.find(item => item.id === this.selectedSchoolYearId) ?? null;
  }

  rolloverTargetSchoolYear(): SchoolYear | null {
    return this.schoolYears.find(item => item.id === this.rolloverDraft.targetSchoolYearId) ?? null;
  }

  unplannedDragData(instance: TopicInstance): PlannerDragData {
    return { kind: 'unplanned', courseId: instance.courseId, instance };
  }

  scheduledDragData(topic: ScheduledTopic, sourceDate: string): PlannerDragData {
    return { kind: 'scheduled', courseId: topic.courseId, topic, sourceDate };
  }

  canDropOnDay(day: CalendarDay, courseId = this.selectedCourseId): boolean {
    const courseDay = this.dayForCourse(courseId, day.date) ?? day;
    return !this.busy && !!courseId && this.isCourseSelected(courseId) &&
      courseDay.isInPlanningRange && courseDay.isCourseDay && courseDay.state === EffectiveDayState.Normal;
  }

  canAnySelectedCourseDropOnDay(day: CalendarDay): boolean {
    return this.selectedCourseIds.some(courseId => this.canDropOnDay(day, courseId));
  }

  onDayDrop(event: CdkDragDrop<CalendarDay, unknown, PlannerDragData>, day: CalendarDay): void {
    const dragged = event.item.data;
    if (!this.canDropOnDay(day, dragged.courseId)) return;

    if (dragged.kind === 'unplanned') {
      this.placeTopic(dragged.instance, day.date);
      return;
    }

    if (dragged.sourceDate === day.date) return;
    this.dragScheduledTopic(dragged.topic, day.date);
  }

  onTopicListDrop(event: CdkDragDrop<TopicInstance[], unknown, PlannerDragData>): void {
    const dragged = event.item.data;
    if (dragged.kind !== 'scheduled' || !this.isCourseSelected(dragged.courseId) || this.busy) return;
    this.removeScheduledTopic(dragged.topic);
  }

  placeTopic(instance: TopicInstance, date = this.placementDate): void {
    if (!this.isCourseSelected(instance.courseId) || !date) {
      this.fail('Choose a target lesson date first.');
      return;
    }

    this.runPlanningCommand(this.planningApi.place({
      topicInstanceId: instance.id,
      courseId: instance.courseId,
      date,
      insertShiftsSchedule: this.editShiftsSchedule,
    }), `Placed “${instance.heading}”`);
  }

  dragScheduledTopic(
    topic: ScheduledTopic,
    destinationDate: string,
    options = {
      deleteShiftsSchedule: this.editShiftsSchedule,
      insertShiftsSchedule: this.editShiftsSchedule,
    },
  ): void {
    this.runPlanningCommand(this.planningApi.drag({
      assignmentId: topic.assignmentId,
      destinationDate,
      deleteShiftsSchedule: options.deleteShiftsSchedule,
      insertShiftsSchedule: options.insertShiftsSchedule,
    }), `Moved “${topic.heading}”`);
  }

  removeScheduledTopic(topic: ScheduledTopic): void {
    this.runPlanningCommand(this.planningApi.remove({
      assignmentId: topic.assignmentId,
      deleteShiftsSchedule: this.editShiftsSchedule,
    }), `Removed “${topic.heading}”`);
  }

  addAllTopics(): void {
    if (!this.topicCourseId || !this.isCourseSelected(this.topicCourseId)) return;
    this.runMultipleTopicPlanningCommand(
      this.planningApi.addAll({
        courseId: this.topicCourseId,
        from: this.multiplePlanningFrom || null,
        until: this.multiplePlanningUntil || null,
      }),
      'added to the schedule',
    );
  }

  removeAllTopics(): void {
    if (!this.topicCourseId || !this.isCourseSelected(this.topicCourseId)) return;
    this.runMultipleTopicPlanningCommand(
      this.planningApi.removeAll({
        courseId: this.topicCourseId,
        from: this.multiplePlanningFrom || null,
        until: this.multiplePlanningUntil || null,
      }),
      'removed from the schedule',
    );
  }

  private runMultipleTopicPlanningCommand(
    request: Observable<MultipleTopicPlanningResult>,
    action: string,
  ): void {
    this.busy = true;
    request.subscribe({
      next: result => {
        this.succeed(`${result.affectedTopicCount} ${result.affectedTopicCount === 1 ? 'topic' : 'topics'} ${action}.`);
        this.reloadCalendar();
      },
      error: error => this.handleError(error),
    });
  }

  copyScheduledTopic(topic: ScheduledTopic): void {
    this.busy = true;
    this.topicApi.copyScheduledInstance(topic.topicInstanceId).subscribe({
      next: () => {
        this.succeed(`Copied “${topic.heading}” to the unplanned topic list.`);
        this.reloadCalendar();
      },
      error: error => this.handleError(error),
    });
  }

  canMoveScheduled(sourceDate: string, direction: -1 | 1, courseId = this.selectedCourseId): boolean {
    return this.relativeLessonDate(sourceDate, direction, courseId) !== null;
  }

  moveScheduled(topic: ScheduledTopic, sourceDate: string, direction: -1 | 1): void {
    const destination = this.relativeLessonDate(sourceDate, direction, topic.courseId);
    if (!destination) {
      this.fail(`There is no ${direction < 0 ? 'earlier' : 'later'} eligible lesson day in the calendar.`);
      return;
    }

    if (direction > 0) {
      this.dragScheduledTopic(topic, destination, {
        deleteShiftsSchedule: false,
        insertShiftsSchedule: true,
      });
      return;
    }

    this.dragScheduledTopic(topic, destination);
  }

  private relativeLessonDate(sourceDate: string, direction: -1 | 1, courseId: string): string | null {
    const calendar = this.courseCalendars[courseId] ??
      (this.selectedCourseIds.length === 1 && this.selectedCourseId === courseId ? this.calendar : null);
    if (!calendar || !this.isCourseSelected(courseId)) return null;
    const eligibleDates = calendar.weeks
      .flatMap(week => week.days)
      .filter(day => day.isInPlanningRange && day.isCourseDay && day.state === EffectiveDayState.Normal)
      .map(day => day.date);
    const index = eligibleDates.indexOf(sourceDate);
    return index >= 0 ? eligibleDates[index + direction] ?? null : null;
  }

  private runPlanningCommand(request: Observable<PlanningImpact>, action: string): void {
    this.busy = true;
    request.subscribe({
      next: impact => {
        const details = [
          impact.movedAssignments.length > 0 ? `${impact.movedAssignments.length} shifted` : '',
          impact.becameUnplanned.length > 0 ? `${impact.becameUnplanned.length} returned to the list` : '',
        ].filter(Boolean).join(', ');
        this.succeed(`${action}${details ? ` (${details})` : ''}.`);
        this.reloadCalendar();
      },
      error: error => this.handleError(error),
    });
  }

  exportData(): void {
    if (this.dataTransferKind === 'topics' && !this.selectedCourseId) {
      this.fail('Select a course before exporting topics.');
      return;
    }

    const records = this.dataTransferKind === 'topics'
      ? this.topics.map(topic => ({ name: topic.heading, description: topic.description }))
      : this.coursesForSelectedSchoolYear().map(course => ({ name: course.name, description: course.description }));
    this.dataTransferText = writeNameDescriptionCsv(records);
    this.succeed(`Exported ${records.length} ${this.dataTransferKind}.`);
  }

  importData(): void {
    if (this.dataTransferKind === 'topics' && !this.selectedCourseId) {
      this.fail('Select a course before importing topics.');
      return;
    }

    let records;
    try {
      records = parseNameDescriptionCsv(this.dataTransferText);
    } catch (error) {
      this.fail(error instanceof Error ? error.message : 'Invalid CSV data.');
      return;
    }

    if (records.length === 0) {
      this.fail('Enter at least one non-empty line to import.');
      return;
    }

    const duplicateImportName = this.findDuplicateName(records.map(record => record.name));
    if (duplicateImportName) {
      this.fail(`The import contains the name “${duplicateImportName}” more than once.`);
      return;
    }

    const existingItems = this.dataTransferKind === 'topics' ? this.topics : this.coursesForSelectedSchoolYear();
    const ambiguousName = records
      .map(record => record.name)
      .find(name => existingItems.filter(item =>
        this.normalizeName('heading' in item ? item.heading : item.name) === this.normalizeName(name)).length > 1);
    if (ambiguousName) {
      this.fail(`More than one existing ${this.dataTransferKind === 'topics' ? 'topic' : 'course'} is named “${ambiguousName}”.`);
      return;
    }

    let updatedCount = 0;
    const requests = this.dataTransferKind === 'topics'
      ? records.map(record => {
          const existing = this.topics.find(topic =>
            this.normalizeName(topic.heading) === this.normalizeName(record.name));
          if (existing) {
            updatedCount += 1;
            return this.topicApi.updateTopic(existing.id, {
              courseId: this.selectedCourseId,
              heading: record.name,
              description: record.description,
            });
          }
          return this.topicApi.createTopic({
            courseId: this.selectedCourseId,
            heading: record.name,
            description: record.description,
          });
        })
      : records.map(record => {
          const existing = this.coursesForSelectedSchoolYear().find(course =>
            this.normalizeName(course.name) === this.normalizeName(record.name));
          if (existing) {
            updatedCount += 1;
          return this.api.updateCourse(existing.id, {
              schoolYearId: existing.schoolYearId,
              name: record.name,
              description: record.description,
              weekdays: [...existing.weekdays],
            });
          }
          return this.api.createCourse({
            schoolYearId: this.selectedSchoolYearId,
            name: record.name,
            description: record.description,
            weekdays: [...(this.config?.visibleWeekdays ?? [
              IsoWeekday.Monday,
              IsoWeekday.Tuesday,
              IsoWeekday.Wednesday,
              IsoWeekday.Thursday,
              IsoWeekday.Friday,
            ])],
          });
        });

    this.busy = true;
    forkJoin(requests).subscribe({
      next: () => {
        const createdCount = records.length - updatedCount;
        this.succeed(
          `Imported ${records.length} ${this.dataTransferKind}: ${updatedCount} updated, ${createdCount} created.`,
        );
        if (this.dataTransferKind === 'topics') {
          this.reloadCalendar();
        } else {
          this.reloadAll();
        }
      },
      error: error => this.handleError(error),
    });
  }

  hasUnplannedTopicsForTopicCourse(): boolean {
    return this.unplannedTopics.some(topic => topic.courseId === this.topicCourseId);
  }

  hasPlannedTopicsForTopicCourse(): boolean {
    return (this.courseCalendars[this.topicCourseId]?.planningSummary?.plannedTopicCount ?? 0) > 0;
  }

  private combineCourseRequests<T>(requests: Observable<T[]>[]): Observable<T[]> {
    if (requests.length === 0) return of([]);
    return forkJoin(requests).pipe(map(results => results.flat()));
  }

  private mergeCourseCalendars(calendars: CalendarView[]): CalendarView {
    if (calendars.length === 1) return calendars[0];

    const base = calendars[0];
    const summaries = calendars
      .map(calendar => calendar.planningSummary)
      .filter(summary => summary !== null);
    return {
      ...base,
      courseId: null,
      planningSummary: {
        lessonDayCount: summaries.reduce((total, summary) => total + summary.lessonDayCount, 0),
        plannedTopicCount: summaries.reduce((total, summary) => total + summary.plannedTopicCount, 0),
        unplannedTopicCount: summaries.reduce((total, summary) => total + summary.unplannedTopicCount, 0),
      },
      weeks: base.weeks.map(week => ({
        ...week,
        days: week.days.map(day => {
          const courseDays = calendars
            .map(calendar => calendar.weeks
              .flatMap(item => item.days)
              .find(item => item.date === day.date))
            .filter(item => item !== undefined);
          const globalFixedDay = courseDays.find(item =>
            item.state === EffectiveDayState.Holiday || item.state === EffectiveDayState.Event);
          return {
            ...day,
            isCourseDay: courseDays.some(item => item.isCourseDay),
            state: globalFixedDay?.state ?? EffectiveDayState.Normal,
            label: globalFixedDay?.label ?? null,
            scheduledTopics: courseDays.flatMap(item => item.scheduledTopics),
            scheduledExams: courseDays.flatMap(item => item.scheduledExams),
          };
        }),
      })),
    };
  }

  private sortTopics<T extends { id: string; heading: string }>(items: T[]): T[] {
    return [...items].sort((left, right) => {
      const leftNumber = this.topicNumberPrefix(left.heading);
      const rightNumber = this.topicNumberPrefix(right.heading);
      if (leftNumber !== null && rightNumber !== null) {
        return leftNumber.length - rightNumber.length ||
          this.compareOrdinal(leftNumber, rightNumber) ||
          this.compareOrdinal(left.id, right.id);
      }
      if (leftNumber !== null) return -1;
      if (rightNumber !== null) return 1;
      return this.compareOrdinal(left.heading.toLowerCase(), right.heading.toLowerCase()) ||
        this.compareOrdinal(left.heading, right.heading) ||
        this.compareOrdinal(left.id, right.id);
    });
  }

  private topicNumberPrefix(heading: string): string | null {
    const match = /^(\d+) /.exec(heading);
    return match ? match[1].replace(/^0+(?=\d)/, '') : null;
  }

  private compareOrdinal(left: string, right: string): number {
    return left < right ? -1 : left > right ? 1 : 0;
  }

  private dayForCourse(courseId: string, date: string): CalendarDay | null {
    return this.courseCalendars[courseId]?.weeks
      .flatMap(week => week.days)
      .find(day => day.date === date) ?? null;
  }

  private syncTopicCourse(): void {
    if (!this.selectedCourseIds.includes(this.topicCourseId)) {
      this.topicCourseId = this.selectedCourseIds[0] ?? '';
    }
  }

  private normalizeName(name: string): string {
    return name.trim().toLocaleLowerCase();
  }

  private syncRolloverOptions(): void {
    const sourceCourses = this.rolloverSourceCourses();
    if (this.selectedCourseId && sourceCourses.some(course => course.id === this.selectedCourseId)) {
      this.rolloverDraft.sourceCourseId = this.selectedCourseId;
    } else if (!sourceCourses.some(course => course.id === this.rolloverDraft.sourceCourseId)) {
      this.rolloverDraft.sourceCourseId = sourceCourses[0]?.id ?? '';
    }

    const targets = this.rolloverTargetSchoolYears();
    if (!targets.some(schoolYear => schoolYear.id === this.rolloverDraft.targetSchoolYearId)) {
      this.rolloverDraft.targetSchoolYearId = targets[0]?.id ?? '';
      this.changeRolloverTargetYear();
    } else if (!this.rolloverDraft.targetStartDate) {
      this.changeRolloverTargetYear();
    }
  }

  private findDuplicateName(names: string[]): string | null {
    const seen = new Set<string>();
    for (const name of names) {
      const normalized = this.normalizeName(name);
      if (seen.has(normalized)) return name;
      seen.add(normalized);
    }
    return null;
  }

  toggleWeekday(target: IsoWeekday[], weekday: IsoWeekday, checked: boolean): void {
    const index = target.indexOf(weekday);
    if (checked && index < 0) target.push(weekday);
    if (!checked && index >= 0) target.splice(index, 1);
    target.sort((left, right) => left - right);
  }

  weekdayName(day: IsoWeekday): string { return IsoWeekday[day].slice(0, 3); }
  calendarDateLabel(date: string): string {
    const match = /^(\d{4})-(\d{2})-(\d{2})$/.exec(date);
    if (!match) return date;

    const [, year, month, day] = match;
    return new Intl.DateTimeFormat('en-GB', {
      day: 'numeric',
      month: 'short',
      timeZone: 'UTC',
    }).format(new Date(Date.UTC(Number(year), Number(month) - 1, Number(day))));
  }
  courseWeekdays(course: Course): string { return course.weekdays.map(day => this.weekdayName(day)).join(', '); }
  markerTypeName(type: GlobalDayMarkerType): string { return GlobalDayMarkerType[type]; }
  stateName(state: EffectiveDayState): string { return EffectiveDayState[state]; }

  dayStyle(day: CalendarDay): Record<string, string> {
    if (!this.config) return {};
    const color = day.state === EffectiveDayState.Holiday ? this.config.holidayColor
      : day.state === EffectiveDayState.Event ? this.config.eventColor
      : day.state === EffectiveDayState.Exam ? this.config.examColor
      : day.scheduledExams.length > 0 ? this.config.examColor
      : '';
    return color ? { 'border-left-color': color } : {};
  }

  private succeed(message: string): void {
    this.message = message;
    this.error = '';
    this.busy = false;
    this.snackBar.open(message, 'Dismiss', { duration: 5000, verticalPosition: 'top' });
    this.changeDetector.markForCheck();
  }

  private handleError(error: HttpErrorResponse): void {
    this.error = error.error?.detail ?? error.message ?? 'Request failed.';
    this.message = '';
    this.busy = false;
    this.changeDetector.markForCheck();
  }

  private fail(message: string): void {
    this.error = message;
    this.message = '';
    this.busy = false;
    this.changeDetector.markForCheck();
  }
}
