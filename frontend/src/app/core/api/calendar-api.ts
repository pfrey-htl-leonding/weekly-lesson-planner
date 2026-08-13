import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';

export enum IsoWeekday {
  Monday = 1,
  Tuesday,
  Wednesday,
  Thursday,
  Friday,
  Saturday,
  Sunday,
}

export enum GlobalDayMarkerType {
  Holiday = 1,
  Event,
}

export enum EffectiveDayState {
  Normal = 0,
  Holiday,
  Event,
  Exam,
}

export interface AppConfig {
  planningStart: string;
  planningEnd: string;
  visibleWeekdays: IsoWeekday[];
  holidayColor: string;
  eventColor: string;
  examColor: string;
  weekNumbering: string;
}

export type SaveAppConfig = Omit<AppConfig, 'weekNumbering'>;

export interface Course {
  id: string;
  name: string;
  description: string;
  weekdays: IsoWeekday[];
}

export type SaveCourse = Omit<Course, 'id'>;

export interface GlobalDayMarker {
  id: string;
  date: string;
  type: GlobalDayMarkerType;
  label: string | null;
}

export type SaveGlobalDayMarker = Omit<GlobalDayMarker, 'id'>;

export interface SaveGlobalDayMarkerRange {
  from: string;
  until: string;
  type: GlobalDayMarkerType;
  label: string | null;
}

export interface CourseExam {
  id: string;
  courseId: string;
  date: string;
  name: string;
}

export type SaveCourseExam = Omit<CourseExam, 'id'>;

export interface CalendarDay {
  date: string;
  weekday: IsoWeekday;
  isInPlanningRange: boolean;
  isCourseDay: boolean;
  state: EffectiveDayState;
  label: string | null;
  scheduledTopics: ScheduledTopic[];
}

export interface ScheduledTopic {
  assignmentId: string;
  topicInstanceId: string;
  courseId: string;
  courseName: string;
  heading: string;
  description: string;
}

export interface CalendarWeek {
  isoYear: number;
  isoWeek: number;
  days: CalendarDay[];
}

export interface CalendarView {
  planningStart: string;
  planningEnd: string;
  courseId: string | null;
  visibleWeekdays: IsoWeekday[];
  weeks: CalendarWeek[];
}

@Injectable({ providedIn: 'root' })
export class CalendarApi {
  private readonly api = inject(ApiClient);

  getConfig(): Observable<AppConfig> { return this.api.get('/api/config'); }
  updateConfig(command: SaveAppConfig): Observable<AppConfig> { return this.api.put('/api/config', command); }
  getCourses(): Observable<Course[]> { return this.api.get('/api/courses'); }
  createCourse(command: SaveCourse): Observable<Course> { return this.api.post('/api/courses', command); }
  updateCourse(id: string, command: SaveCourse): Observable<Course> { return this.api.put(`/api/courses/${id}`, command); }
  deleteCourse(id: string): Observable<void> { return this.api.delete(`/api/courses/${id}`); }
  getMarkers(): Observable<GlobalDayMarker[]> { return this.api.get('/api/global-markers'); }
  createMarker(command: SaveGlobalDayMarker): Observable<GlobalDayMarker> { return this.api.post('/api/global-markers', command); }
  createMarkerRange(command: SaveGlobalDayMarkerRange): Observable<GlobalDayMarker[]> {
    return this.api.post('/api/global-markers/range', command);
  }
  updateMarker(id: string, command: SaveGlobalDayMarker): Observable<GlobalDayMarker> { return this.api.put(`/api/global-markers/${id}`, command); }
  deleteMarker(id: string): Observable<void> { return this.api.delete(`/api/global-markers/${id}`); }
  getExams(courseId?: string): Observable<CourseExam[]> {
    return this.api.get(`/api/course-exams${courseId ? `?courseId=${courseId}` : ''}`);
  }
  createExam(command: SaveCourseExam): Observable<CourseExam> { return this.api.post('/api/course-exams', command); }
  updateExam(id: string, command: SaveCourseExam): Observable<CourseExam> { return this.api.put(`/api/course-exams/${id}`, command); }
  deleteExam(id: string): Observable<void> { return this.api.delete(`/api/course-exams/${id}`); }
  getCalendar(courseId?: string): Observable<CalendarView> {
    return this.api.get(`/api/calendar${courseId ? `?courseId=${courseId}` : ''}`);
  }
}
