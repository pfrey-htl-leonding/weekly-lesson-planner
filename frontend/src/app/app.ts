import { CommonModule } from '@angular/common';
import { ChangeDetectorRef, Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
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
import { forkJoin, of } from 'rxjs';
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
} from './core/api/calendar-api';
import { SaveTopic, TopicApi, TopicDefinition, TopicInstance } from './core/api/topic-api';
import {
  parseNameDescriptionCsv,
  writeNameDescriptionCsv,
} from './core/data/name-description-csv';

@Component({
  selector: 'app-root',
  imports: [
    CommonModule,
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
  private readonly snackBar = inject(MatSnackBar);
  private readonly changeDetector = inject(ChangeDetectorRef);

  readonly weekdays = Object.values(IsoWeekday).filter((value): value is IsoWeekday => typeof value === 'number');
  readonly markerTypes = GlobalDayMarkerType;
  readonly states = EffectiveDayState;

  config: AppConfig | null = null;
  courses: Course[] = [];
  markers: GlobalDayMarker[] = [];
  exams: CourseExam[] = [];
  calendar: CalendarView | null = null;
  topics: TopicDefinition[] = [];
  unplannedTopics: TopicInstance[] = [];
  selectedCourseId = '';
  courseDraft: SaveCourse = { name: '', description: '', weekdays: [] };
  editingCourseId: string | null = null;
  markerDraft = { date: '', until: '', type: GlobalDayMarkerType.Holiday, label: '' };
  editingMarkerId: string | null = null;
  examDraft = { date: '', name: '' };
  editingExamId: string | null = null;
  topicDraft: Omit<SaveTopic, 'courseId'> = { heading: '', description: '' };
  editingTopicId: string | null = null;
  topicSearch = '';
  dataTransferText = '';
  dataTransferKind: 'topics' | 'courses' = 'topics';
  busy = false;
  message = '';
  error = '';

  ngOnInit(): void {
    this.reloadAll();
  }

  reloadAll(): void {
    this.busy = true;
    forkJoin({
      config: this.api.getConfig(),
      courses: this.api.getCourses(),
      markers: this.api.getMarkers(),
    }).subscribe({
      next: ({ config, courses, markers }) => {
        this.config = config;
        this.courses = courses;
        this.markers = markers;
        if (this.selectedCourseId && !courses.some(course => course.id === this.selectedCourseId)) {
          this.selectedCourseId = '';
        }
        this.changeDetector.markForCheck();
        this.reloadCalendar();
      },
      error: error => this.handleError(error),
    });
  }

  reloadCalendar(): void {
    this.busy = true;
    forkJoin({
      calendar: this.api.getCalendar(this.selectedCourseId || undefined),
      exams: this.selectedCourseId ? this.api.getExams(this.selectedCourseId) : of([]),
      topics: this.selectedCourseId ? this.topicApi.getTopics(this.selectedCourseId) : of<TopicDefinition[]>([]),
      unplannedTopics: this.selectedCourseId
        ? this.topicApi.getUnplannedInstances(this.selectedCourseId)
        : of<TopicInstance[]>([]),
    }).subscribe({
      next: ({ calendar, exams, topics, unplannedTopics }) => {
        this.calendar = calendar;
        this.exams = exams;
        this.topics = topics;
        this.unplannedTopics = unplannedTopics;
        this.busy = false;
        this.error = '';
        this.changeDetector.markForCheck();
      },
      error: error => this.handleError(error),
    });
  }

  changeCourseView(): void {
    this.clearExam();
    this.clearTopic();
    this.reloadCalendar();
  }

  saveConfig(): void {
    if (!this.config) return;
    this.busy = true;
    this.api.updateConfig({
      planningStart: this.config.planningStart,
      planningEnd: this.config.planningEnd,
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

  selectCourse(course: Course | null): void {
    if (!course) {
      this.editingCourseId = null;
      this.courseDraft = { name: '', description: '', weekdays: [] };
      return;
    }
    this.editingCourseId = course.id;
    this.courseDraft = { name: course.name, description: course.description, weekdays: [...course.weekdays] };
  }

  saveCourse(): void {
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

  exportData(): void {
    if (this.dataTransferKind === 'topics' && !this.selectedCourseId) {
      this.fail('Select a course before exporting topics.');
      return;
    }

    const records = this.dataTransferKind === 'topics'
      ? this.topics.map(topic => ({ name: topic.heading, description: topic.description }))
      : this.courses.map(course => ({ name: course.name, description: course.description }));
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

    const existingItems = this.dataTransferKind === 'topics' ? this.topics : this.courses;
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
          const existing = this.courses.find(course =>
            this.normalizeName(course.name) === this.normalizeName(record.name));
          if (existing) {
            updatedCount += 1;
            return this.api.updateCourse(existing.id, {
              name: record.name,
              description: record.description,
              weekdays: [...existing.weekdays],
            });
          }
          return this.api.createCourse({
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
