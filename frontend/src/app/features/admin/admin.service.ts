import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { API_URL } from '../../core/api-config';
import { Role } from '../../core/auth/auth.models';
import { UserSummary } from '../../core/user.models';

export interface NewUser {
  email: string;
  password: string;
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

  resetPassword(id: string, newPassword: string): Observable<UserSummary> {
    return this.http.put<UserSummary>(`${API_URL}/users/${id}/password`, { newPassword });
  }
}
