import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_URL } from '../../core/api-config';
import { DashboardStatistics, DashboardSummary } from './dashboard.models';

/** Talks to the dashboard endpoints of the API. */
@Injectable({ providedIn: 'root' })
export class DashboardService {
  private readonly http = inject(HttpClient);

  getSummary(): Observable<DashboardSummary> {
    return this.http.get<DashboardSummary>(`${API_URL}/dashboard/summary`);
  }

  getStatistics(days: number): Observable<DashboardStatistics> {
    return this.http.get<DashboardStatistics>(`${API_URL}/dashboard/incident-statistics`, {
      params: new HttpParams().set('days', days),
    });
  }
}
