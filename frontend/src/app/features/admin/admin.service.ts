import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_URL } from '../../core/api-config';
import { Role } from '../../core/auth/auth.models';
import { Category } from '../../core/category.models';
import { UserSummary } from '../../core/user.models';
import { IncidentPriority } from '../incidents/incident.models';
import { SlaPolicy } from './sla/sla.models';

export interface NewUser {
  email: string;
  role: Role;
}

/** The Admin-only calls of the API. */
@Injectable({ providedIn: 'root' })
export class AdminService {
  private readonly http = inject(HttpClient);

  getUsers(): Observable<UserSummary[]> {
    return this.http.get<UserSummary[]>(`${API_URL}/users`);
  }

  createUser(user: NewUser): Observable<UserSummary> {
    return this.http.post<UserSummary>(`${API_URL}/users`, user);
  }

  changeRole(id: string, role: Role): Observable<UserSummary> {
    return this.http.put<UserSummary>(`${API_URL}/users/${id}/role`, { role });
  }

  setActive(id: string, isActive: boolean): Observable<UserSummary> {
    return this.http.put<UserSummary>(`${API_URL}/users/${id}/active`, { isActive });
  }

  /** Emails the person a link to choose a new password. The Admin never sees or types a password. */
  sendPasswordResetEmail(id: string): Observable<UserSummary> {
    return this.http.post<UserSummary>(`${API_URL}/users/${id}/password-reset-email`, {});
  }

  getSlaPolicies(): Observable<SlaPolicy[]> {
    return this.http.get<SlaPolicy[]>(`${API_URL}/sla/policies`);
  }

  updateSlaPolicy(priority: IncidentPriority, change: { resolutionTargetHours: number; isActive: boolean }): Observable<SlaPolicy> {
    return this.http.put<SlaPolicy>(`${API_URL}/sla/policies/${priority}`, change);
  }

  /** Every category, including hidden ones (the normal list leaves those out). */
  getCategories(): Observable<Category[]> {
    return this.http.get<Category[]>(`${API_URL}/categories`, { params: new HttpParams().set('includeInactive', true) });
  }

  createCategory(name: string, description: string | null): Observable<Category> {
    return this.http.post<Category>(`${API_URL}/categories`, { name, description });
  }

  /** The API replaces all three fields at once, so hiding a category also sends its current name and description. */
  updateCategory(id: string, change: { name: string; description: string | null; isActive: boolean }): Observable<Category> {
    return this.http.put<Category>(`${API_URL}/categories/${id}`, change);
  }
}
