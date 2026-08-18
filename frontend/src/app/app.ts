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
import { MatSelectModule } from '@angular/material/select';
import { MatRadioModule } from '@angular/material/radio';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTabsModule } from '@angular/material/tabs';
import { MatToolbarModule } from '@angular/material/toolbar';
import { forkJoin, Observable, of } from 'rxjs';
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

  config: AppConfig | null = null;
  schoolYears: SchoolYear[] = [];
  courses: Course[] = [];
  markers: GlobalDayMarker[] = [];
  exams: CourseExam[] = [];
  calendar: CalendarView | null = null;
  topics: TopicDefinition[] = [];
  unplannedTopics: TopicInstance[] = [];
  selectedCourseId = '';
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
  topicDraft: Omit<SaveTopic, 'courseId'> = { heading: '', description: '' };
  editingTopicId: string | null = null;
  topicSearch = '';
  placementDate = '';
  multiplePlanningFrom = '';
  multiplePlanningUntil = '';
  insertShiftsSchedule = false;
  deleteShiftsSchedule = false;
  dataTransferText = '';
  dataTransferKind: 'topics' | 'courses' = 'topics';
  busy = false;
  message = '';
  error = '';

  readonly canEnterDay = (
    drag: CdkDrag<PlannerDragData>,
    drop: CdkDropList<CalendarDay>,
  ): boolean => this.canDropOnDay(drop.data, drag.data.courseId);

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
        if (this.selectedCourseId && !courses.some(course => course.id === this.selectedCourseId)) {
          this.selectedCourseId = '';
        }
        if (this.selectedCourseId) {
          this.selectedSchoolYearId = courses.find(course => course.id === this.selectedCourseId)?.schoolYearId ?? this.selectedSchoolYearId;
        }
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
    forkJoin({
      calendar: this.api.getCalendar(this.selectedCourseId || undefined, this.selectedSchoolYearId || undefined),
      markers: this.selectedSchoolYearId ? this.api.getMarkers(this.selectedSchoolYearId) : of([]),
      exams: this.selectedCourseId ? this.api.getExams(this.selectedCourseId) : of([]),
      topics: this.selectedCourseId ? this.topicApi.getTopics(this.selectedCourseId) : of<TopicDefinition[]>([]),
      unplannedTopics: this.selectedCourseId
        ? this.topicApi.getUnplannedInstances(this.selectedCourseId)
        : of<TopicInstance[]>([]),
    }).subscribe({
      next: ({ calendar, markers, exams, topics, unplannedTopics }) => {
        this.calendar = calendar;
        this.markers = markers;
        this.exams = exams;
        this.topics = topics;
        this.unplannedTopics = unplannedTopics;
        if (this.selectedCourseId) {
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
    if (this.selectedCourseId) {
      this.selectedSchoolYearId = this.courses.find(course => course.id === this.selectedCourseId)?.schoolYearId ?? this.selectedSchoolYearId;
      this.rolloverDraft.sourceCourseId = this.selectedCourseId;
    }
    this.clearExam();
    this.clearTopic();
    this.reloadCalendar();
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
    this.selectedCourseId = '';
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
        if (this.selectedCourseId === course.id) this.selectedCourseId = '';
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
    this.examDraft = { date: exam.date, name: exam.name };
  }

  clearExam(): void {
    this.editingExamId = null;
    this.examDraft = { date: '', name: '' };
  }

  saveExam(): void {
    if (!this.selectedCourseId) return;
    const command = { courseId: this.selectedCourseId, ...this.examDraft };
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

  deleteExam(exam: CourseExam): void {
    this.api.deleteExam(exam.id).subscribe({
      next: () => { this.succeed('Exam deleted.'); this.reloadAll(); },
      error: error => this.handleError(error),
    });
  }

  editTopic(topic: TopicDefinition | TopicInstance): void {
    this.editingTopicId = 'topicId' in topic ? topic.topicId : topic.id;
    this.topicDraft = { heading: topic.heading, description: topic.description };
  }

  clearTopic(): void {
    this.editingTopicId = null;
    this.topicDraft = { heading: '', description: '' };
  }

  saveTopic(): void {
    if (!this.selectedCourseId) return;
    const command: SaveTopic = { courseId: this.selectedCourseId, ...this.topicDraft };
    const wasEditing = this.editingTopicId !== null;
    const request = this.editingTopicId
      ? this.topicApi.updateTopic(this.editingTopicId, command)
      : this.topicApi.createTopic(command);
    this.busy = true;
    request.subscribe({
      next: topic => {
        this.clearTopic();
        this.topicSearch = '';
        forkJoin({
          topics: this.topicApi.getTopics(this.selectedCourseId),
          unplannedTopics: this.topicApi.getUnplannedInstances(this.selectedCourseId),
        }).subscribe({
          next: ({ topics, unplannedTopics }) => {
            this.topics = topics;
            this.unplannedTopics = unplannedTopics;
            this.succeed(wasEditing
              ? `Topic “${topic.heading}” updated.`
              : `Topic “${topic.heading}” added to the unplanned list.`);
          },
          error: error => this.handleError(error),
        });
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
    return !this.busy && !!this.selectedCourseId && courseId === this.selectedCourseId &&
      day.isInPlanningRange && day.isCourseDay && day.state === EffectiveDayState.Normal;
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
    if (dragged.kind !== 'scheduled' || dragged.courseId !== this.selectedCourseId || this.busy) return;
    this.removeScheduledTopic(dragged.topic);
  }

  placeTopic(instance: TopicInstance, date = this.placementDate): void {
    if (!this.selectedCourseId || !date) {
      this.fail('Choose a target lesson date first.');
      return;
    }

    this.runPlanningCommand(this.planningApi.place({
      topicInstanceId: instance.id,
      courseId: this.selectedCourseId,
      date,
      insertShiftsSchedule: this.insertShiftsSchedule,
    }), `Placed “${instance.heading}”`);
  }

  dragScheduledTopic(
    topic: ScheduledTopic,
    destinationDate: string,
    options = {
      deleteShiftsSchedule: this.deleteShiftsSchedule,
      insertShiftsSchedule: this.insertShiftsSchedule,
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
      deleteShiftsSchedule: this.deleteShiftsSchedule,
    }), `Removed “${topic.heading}”`);
  }

  addAllTopics(): void {
    if (!this.selectedCourseId) return;
    this.runMultipleTopicPlanningCommand(
      this.planningApi.addAll({
        courseId: this.selectedCourseId,
        from: this.multiplePlanningFrom || null,
        until: this.multiplePlanningUntil || null,
      }),
      'added to the schedule',
    );
  }

  removeAllTopics(): void {
    if (!this.selectedCourseId) return;
    this.runMultipleTopicPlanningCommand(
      this.planningApi.removeAll({
        courseId: this.selectedCourseId,
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

  canMoveScheduled(sourceDate: string, direction: -1 | 1): boolean {
    return this.relativeLessonDate(sourceDate, direction) !== null;
  }

  moveScheduled(topic: ScheduledTopic, sourceDate: string, direction: -1 | 1): void {
    const destination = this.relativeLessonDate(sourceDate, direction);
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

  private relativeLessonDate(sourceDate: string, direction: -1 | 1): string | null {
    if (!this.calendar || !this.selectedCourseId) return null;
    const eligibleDates = this.calendar.weeks
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
