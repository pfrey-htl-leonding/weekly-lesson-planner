import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class ApiClient {
  private readonly http = inject(HttpClient);

  get<TResponse>(path: string): Observable<TResponse> {
    return this.http.get<TResponse>(path);
  }

  post<TResponse>(path: string, body: unknown): Observable<TResponse> {
    return this.http.post<TResponse>(path, body);
  }

  put<TResponse>(path: string, body: unknown): Observable<TResponse> {
    return this.http.put<TResponse>(path, body);
  }

  delete(path: string): Observable<void> {
    return this.http.delete<void>(path);
  }
}
