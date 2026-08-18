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
  visibleWeekdays: IsoWeekday[];
  holidayColor: string;
  eventColor: string;
  examColor: string;
  weekNumbering: string;
}

export type SaveAppConfig = Omit<AppConfig, 'weekNumbering'>;

export interface SchoolYear {
  id: string;
  name: string;
  planningStart: string;
  planningEnd: string;
}

export type SaveSchoolYear = Omit<SchoolYear, 'id'>;

export interface Course {
  id: string;
  schoolYearId: string;
  name: string;
  description: string;
  weekdays: IsoWeekday[];
}

export type SaveCourse = Omit<Course, 'id'>;

export interface GlobalDayMarker {
  id: string;
  schoolYearId: string;
  date: string;
  type: GlobalDayMarkerType;
  label: string | null;
}

export type SaveGlobalDayMarker = Omit<GlobalDayMarker, 'id'>;

export interface SaveGlobalDayMarkerRange {
  schoolYearId: string;
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

export interface CoursePlanningSummary {
  lessonDayCount: number;
  plannedTopicCount: number;
  unplannedTopicCount: number;
}

export interface CalendarView {
  planningStart: string;
  planningEnd: string;
  schoolYearId: string;
  schoolYearName: string;
  courseId: string | null;
  visibleWeekdays: IsoWeekday[];
  weeks: CalendarWeek[];
  planningSummary: CoursePlanningSummary | null;
}

@Injectable({ providedIn: 'root' })
export class CalendarApi {
  private readonly api = inject(ApiClient);

  getConfig(): Observable<AppConfig> { return this.api.get('/api/config'); }
  updateConfig(command: SaveAppConfig): Observable<AppConfig> { return this.api.put('/api/config', command); }
  getSchoolYears(): Observable<SchoolYear[]> { return this.api.get('/api/school-years'); }
  createSchoolYear(command: SaveSchoolYear): Observable<SchoolYear> { return this.api.post('/api/school-years', command); }
  updateSchoolYear(id: string, command: SaveSchoolYear): Observable<SchoolYear> { return this.api.put(`/api/school-years/${id}`, command); }
  deleteSchoolYear(id: string): Observable<void> { return this.api.delete(`/api/school-years/${id}`); }
  getCourses(schoolYearId?: string): Observable<Course[]> {
    return this.api.get(`/api/courses${schoolYearId ? `?schoolYearId=${schoolYearId}` : ''}`);
  }
  createCourse(command: SaveCourse): Observable<Course> { return this.api.post('/api/courses', command); }
  updateCourse(id: string, command: SaveCourse): Observable<Course> { return this.api.put(`/api/courses/${id}`, command); }
  deleteCourse(id: string): Observable<void> { return this.api.delete(`/api/courses/${id}`); }
  getMarkers(schoolYearId: string): Observable<GlobalDayMarker[]> {
    return this.api.get(`/api/global-markers?schoolYearId=${schoolYearId}`);
  }
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
  getCalendar(courseId?: string, schoolYearId?: string): Observable<CalendarView> {
    const query = new URLSearchParams();
    if (courseId) query.set('courseId', courseId);
    if (schoolYearId) query.set('schoolYearId', schoolYearId);
    return this.api.get(`/api/calendar${query.size ? `?${query.toString()}` : ''}`);
  }
}
