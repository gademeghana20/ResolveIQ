import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, tap } from 'rxjs';

import { API_URL } from '../api-config';
import { AuthResponse, LoginRequest, RegisterRequest, Role } from './auth.models';

const STORAGE_KEY = 'resolveiq.session';

/**
 * Knows who is logged in. Everything else in the app asks this service
 * ("is someone logged in?", "what are their roles?", "what is their token?").
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);

  // The one source of truth. A signal is a value that the screen reacts to automatically when it changes.
  private readonly session = signal<AuthResponse | null>(this.readStoredSession());

  /** The logged-in person's email, or null. */
  readonly email = computed(() => this.session()?.email ?? null);

  /** The logged-in person's roles (empty when logged out). */
  readonly roles = computed<Role[]>(() => this.session()?.roles ?? []);

  login(request: LoginRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_URL}/auth/login`, request)
      .pipe(tap((response) => this.startSession(response)));
  }

  register(request: RegisterRequest): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_URL}/auth/register`, request)
      .pipe(tap((response) => this.startSession(response)));
  }

  logout(): void {
    this.session.set(null);
    this.storage()?.removeItem(STORAGE_KEY);
  }

  /**
   * The token to send to the API, or null if nobody is logged in or it has run out.
   * An expired token is cleaned up here, so the rest of the app never uses one.
   */
  getValidToken(): string | null {
    const current = this.session();
    if (!current) {
      return null;
    }

    if (isExpired(current.token)) {
      this.logout();
      return null;
    }

    return current.token;
  }

  isLoggedIn(): boolean {
    return this.getValidToken() !== null;
  }

  /** True if the logged-in person has at least one of the given roles. */
  hasRole(...allowed: Role[]): boolean {
    return this.isLoggedIn() && this.roles().some((role) => allowed.includes(role));
  }

  private startSession(response: AuthResponse): void {
    this.session.set(response);
    this.storage()?.setItem(STORAGE_KEY, JSON.stringify(response));
  }

  /** Restores a login that survived a page refresh. Anything odd or expired is ignored. */
  private readStoredSession(): AuthResponse | null {
    try {
      const raw = this.storage()?.getItem(STORAGE_KEY);
      if (!raw) {
        return null;
      }

      const parsed = JSON.parse(raw) as Partial<AuthResponse>;
      const looksValid =
        typeof parsed.token === 'string' &&
        typeof parsed.email === 'string' &&
        Array.isArray(parsed.roles);

      if (!looksValid || isExpired(parsed.token!)) {
        this.storage()?.removeItem(STORAGE_KEY);
        return null;
      }

      return parsed as AuthResponse;
    } catch {
      return null;
    }
  }

  // Browsers can block storage (private windows, strict settings), so never assume it works.
  private storage(): Storage | null {
    try {
      return localStorage;
    } catch {
      return null;
    }
  }
}

/**
 * Reads the "exp" (expiry) time inside the token. This is only a convenience so the screen can
 * send you to the login page politely. It does NOT prove the token is genuine: the API always
 * checks the signature itself, and that is the real security.
 */
function isExpired(token: string): boolean {
  try {
    const payload = token.split('.')[1];
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    const exp = (JSON.parse(json) as { exp?: number }).exp;
    return typeof exp !== 'number' || exp * 1000 <= Date.now();
  } catch {
    return true; // not a readable token: treat as unusable
  }
}
