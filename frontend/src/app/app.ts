import { CommonModule } from '@angular/common';
import { Component, OnInit, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatSelectModule } from '@angular/material/select';
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
    MatToolbarModule,
  ],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  private readonly api = inject(CalendarApi);

  readonly weekdays = Object.values(IsoWeekday).filter((value): value is IsoWeekday => typeof value === 'number');
  readonly markerTypes = GlobalDayMarkerType;
  readonly states = EffectiveDayState;

  config: AppConfig | null = null;
  courses: Course[] = [];
  markers: GlobalDayMarker[] = [];
  exams: CourseExam[] = [];
  calendar: CalendarView | null = null;
  selectedCourseId = '';
  courseDraft: SaveCourse = { name: '', description: '', weekdays: [] };
  editingCourseId: string | null = null;
  markerDraft = { date: '', type: GlobalDayMarkerType.Holiday, label: '' };
  editingMarkerId: string | null = null;
  examDraft = { date: '', name: '' };
  editingExamId: string | null = null;
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
    }).subscribe({
      next: ({ calendar, exams }) => {
        this.calendar = calendar;
        this.exams = exams;
        this.busy = false;
        this.error = '';
      },
      error: error => this.handleError(error),
    });
  }

  changeCourseView(): void {
    this.clearExam();
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
    const request = this.editingCourseId
      ? this.api.updateCourse(this.editingCourseId, this.courseDraft)
      : this.api.createCourse(this.courseDraft);
    this.busy = true;
    request.subscribe({
      next: course => {
        this.selectedCourseId = course.id;
        this.selectCourse(null);
        this.succeed('Course saved.');
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
    this.markerDraft = { date: marker.date, type: marker.type, label: marker.label ?? '' };
  }

  clearMarker(): void {
    this.editingMarkerId = null;
    this.markerDraft = { date: '', type: GlobalDayMarkerType.Holiday, label: '' };
  }

  saveMarker(): void {
    const command = { ...this.markerDraft, label: this.markerDraft.label || null };
    const request = this.editingMarkerId
      ? this.api.updateMarker(this.editingMarkerId, command)
      : this.api.createMarker(command);
    request.subscribe({
      next: () => {
        this.clearMarker();
        this.succeed('Global day marker saved.');
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

  toggleWeekday(target: IsoWeekday[], weekday: IsoWeekday, checked: boolean): void {
    const index = target.indexOf(weekday);
    if (checked && index < 0) target.push(weekday);
    if (!checked && index >= 0) target.splice(index, 1);
    target.sort((left, right) => left - right);
  }

  weekdayName(day: IsoWeekday): string { return IsoWeekday[day].slice(0, 3); }
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
  }

  private handleError(error: HttpErrorResponse): void {
    this.error = error.error?.detail ?? error.message ?? 'Request failed.';
    this.message = '';
    this.busy = false;
  }
}
