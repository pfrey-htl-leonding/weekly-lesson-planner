import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';
import { TopicInstance } from './topic-api';

export interface PlaceTopicCommand {
  topicInstanceId: string;
  courseId: string;
  date: string;
  insertShiftsSchedule: boolean;
}

export interface RemoveTopicCommand {
  assignmentId: string;
  deleteShiftsSchedule: boolean;
}

export interface DragTopicCommand {
  assignmentId: string;
  destinationDate: string;
  deleteShiftsSchedule: boolean;
  insertShiftsSchedule: boolean;
}

export interface AssignmentImpact {
  assignmentId: string;
  topicInstanceId: string;
  courseId: string;
  date: string;
  heading: string;
  description: string;
}

export interface AssignmentMove {
  assignmentId: string;
  topicInstanceId: string;
  from: string;
  to: string;
}

export interface PlanningImpact {
  insertedAssignment: AssignmentImpact | null;
  removedAssignment: AssignmentImpact | null;
  movedAssignments: AssignmentMove[];
  affectedDates: string[];
  becameUnplanned: TopicInstance[];
}

@Injectable({ providedIn: 'root' })
export class PlanningApi {
  private readonly api = inject(ApiClient);

  place(command: PlaceTopicCommand): Observable<PlanningImpact> {
    return this.api.post('/api/planning/place', command);
  }

  remove(command: RemoveTopicCommand): Observable<PlanningImpact> {
    return this.api.post('/api/planning/remove', command);
  }

  drag(command: DragTopicCommand): Observable<PlanningImpact> {
    return this.api.post('/api/planning/drag', command);
  }
}
