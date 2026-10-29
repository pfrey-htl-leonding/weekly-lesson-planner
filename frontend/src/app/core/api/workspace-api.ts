import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

export type WorkspaceStatus = 'provisioning' | 'ready' | 'failed' | 'disabled';

export interface WorkspaceSummary {
  id: string;
  slug: string;
  displayName: string;
  status: WorkspaceStatus;
  provisioningError: string | null;
}

export interface CreateWorkspace {
  displayName: string;
  slug: string;
}

@Injectable({ providedIn: 'root' })
export class WorkspaceContext {
  private activeSlug = '';

  get slug(): string { return this.activeSlug; }

  setActive(slug: string): void {
    this.activeSlug = slug;
  }

  scopedApiPath(path: string): string {
    if (!this.activeSlug) throw new Error('No workspace is active.');
    if (!path.startsWith('/api/')) throw new Error(`Expected an API path, received “${path}”.`);
    return `/api/workspaces/${encodeURIComponent(this.activeSlug)}${path.slice(4)}`;
  }
}

@Injectable({ providedIn: 'root' })
export class WorkspaceApi {
  private readonly http = inject(HttpClient);

  list(): Observable<WorkspaceSummary[]> {
    return this.http.get<WorkspaceSummary[]>('/api/workspaces');
  }

  create(command: CreateWorkspace): Observable<WorkspaceSummary> {
    return this.http.post<WorkspaceSummary>('/api/workspaces', command);
  }

  retry(slug: string): Observable<WorkspaceSummary> {
    return this.http.post<WorkspaceSummary>(
      `/api/workspaces/${encodeURIComponent(slug)}/retry-provisioning`, {});
  }

  delete(slug: string): Observable<void> {
    return this.http.delete<void>(`/api/workspaces/${encodeURIComponent(slug)}`);
  }
}
