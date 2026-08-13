import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { ApiClient } from './api-client';

export interface TopicDefinition {
  id: string;
  courseId: string;
  heading: string;
  description: string;
  totalInstanceCount: number;
  plannedInstanceCount: number;
  unplannedInstanceCount: number;
}

export interface SaveTopic {
  courseId: string;
  heading: string;
  description: string;
}

export interface TopicInstance {
  id: string;
  topicId: string;
  courseId: string;
  heading: string;
  description: string;
}

@Injectable({ providedIn: 'root' })
export class TopicApi {
  private readonly api = inject(ApiClient);

  getTopics(courseId?: string): Observable<TopicDefinition[]> {
    return this.api.get(`/api/topics${courseId ? `?courseId=${courseId}` : ''}`);
  }

  createTopic(command: SaveTopic): Observable<TopicDefinition> {
    return this.api.post('/api/topics', command);
  }

  updateTopic(id: string, command: SaveTopic): Observable<TopicDefinition> {
    return this.api.put(`/api/topics/${id}`, command);
  }

  deleteTopic(id: string): Observable<void> {
    return this.api.delete(`/api/topics/${id}`);
  }

  getUnplannedInstances(courseId: string, search?: string): Observable<TopicInstance[]> {
    const query = new URLSearchParams({ courseId });
    if (search) query.set('search', search);
    return this.api.get(`/api/topic-instances/unplanned?${query.toString()}`);
  }

  deleteUnplannedInstance(id: string): Observable<void> {
    return this.api.delete(`/api/topic-instances/${id}`);
  }

  copyScheduledInstance(id: string): Observable<TopicInstance> {
    return this.api.post(`/api/topic-instances/${id}/copy`, {});
  }
}
