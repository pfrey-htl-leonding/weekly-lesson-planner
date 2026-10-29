import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { WorkspaceContext } from './workspace-api';

@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);
  private readonly workspace = inject(WorkspaceContext);

  get<TResponse>(path: string): Observable<TResponse> {
    return this.http.get<TResponse>(this.workspace.scopedApiPath(path));
  }

  post<TResponse>(path: string, body: unknown): Observable<TResponse> {
    return this.http.post<TResponse>(this.workspace.scopedApiPath(path), body);
  }

  put<TResponse>(path: string, body: unknown): Observable<TResponse> {
    return this.http.put<TResponse>(this.workspace.scopedApiPath(path), body);
  }

  delete(path: string): Observable<void> {
    return this.http.delete<void>(this.workspace.scopedApiPath(path));
  }
}
