import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_URL } from '../../core/api-config';
import {
  Category,
  HistoryEntry,
  IncidentComment,
  IncidentDetail,
  IncidentQuery,
  IncidentSummary,
  PagedResult,
} from './incident.models';

/** Talks to the incident endpoints of the API. */
@Injectable({ providedIn: 'root' })
export class IncidentService {
  private readonly http = inject(HttpClient);

  getIncidents(query: IncidentQuery): Observable<PagedResult<IncidentSummary>> {
    return this.http.get<PagedResult<IncidentSummary>>(`${API_URL}/incidents`, { params: toParams(query) });
  }

  getIncident(id: string): Observable<IncidentDetail> {
    return this.http.get<IncidentDetail>(`${API_URL}/incidents/${id}`);
  }

  getHistory(id: string): Observable<HistoryEntry[]> {
    return this.http.get<HistoryEntry[]>(`${API_URL}/incidents/${id}/history`);
  }

  getComments(id: string): Observable<IncidentComment[]> {
    return this.http.get<IncidentComment[]>(`${API_URL}/incidents/${id}/comments`);
  }

  addComment(id: string, text: string): Observable<IncidentComment> {
    return this.http.post<IncidentComment>(`${API_URL}/incidents/${id}/comments`, { text });
  }

  getCategories(): Observable<Category[]> {
    return this.http.get<Category[]>(`${API_URL}/categories`);
  }
}

/** Turns the query object into ?page=1&status=Open... and leaves out anything not set. */
export function toParams(query: IncidentQuery): HttpParams {
  let params = new HttpParams()
    .set('page', query.page)
    .set('pageSize', query.pageSize)
    .set('sortBy', query.sortBy)
    .set('sortDirection', query.sortDirection);

  const search = query.search?.trim();
  if (search) {
    params = params.set('search', search);
  }
  if (query.status) {
    params = params.set('status', query.status);
  }
  if (query.priority) {
    params = params.set('priority', query.priority);
  }
  if (query.categoryId) {
    params = params.set('categoryId', query.categoryId);
  }
  if (query.slaState) {
    params = params.set('slaState', query.slaState);
  }
  if (query.assignedToMe) {
    params = params.set('assignedToMe', true);
  }
  if (query.reportedByMe) {
    params = params.set('reportedByMe', true);
  }

  return params;
}
