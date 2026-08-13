import { inject, Injectable } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiClient } from './api-client';

export interface SystemStatus {
  service: string;
  databaseProvider: string;
  databaseAvailable: boolean;
}

@Injectable({ providedIn: 'root' })
export class SystemApiClient {
  private readonly api = inject(ApiClient);

  getStatus(): Observable<SystemStatus> {
    return this.api.get<SystemStatus>('/api/system/status');
  }
}

