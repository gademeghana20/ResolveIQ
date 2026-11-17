import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, of, shareReplay, tap, throwError } from 'rxjs';

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

  // While a refresh is in flight, everyone who needs one shares it, so the single-use ticket is spent only once.
  private refreshing: Observable<AuthResponse> | null = null;

  /** The logged-in person's email, or null. */
  readonly email = computed(() => this.session()?.email ?? null);

  /** The logged-in person's roles (empty when logged out). */
  readonly roles = computed<Role[]>(() => this.session()?.roles ?? []);

  /**
   * The logged-in person's id, read from their token. Used to tell "this row is me" apart on screens such as
   * the user list. It is for display only; the API always works out who you are from the token itself.
   */
  readonly userId = computed(() => {
    const token = this.session()?.token;
    return token ? (readPayload(token)?.sub ?? null) : null;
  });

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

  /** Changes your own password. The API ends your old login and sends a fresh one, so you stay signed in. */
  changePassword(currentPassword: string, newPassword: string): Observable<AuthResponse> {
    return this.http
      .post<AuthResponse>(`${API_URL}/auth/change-password`, { currentPassword, newPassword })
      .pipe(tap((response) => this.startSession(response)));
  }

  /** "Forgot password": asks for a reset email. The answer is the same whether or not the address has an account. */
  forgotPassword(email: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${API_URL}/auth/forgot-password`, { email });
  }

  /** Chooses a new password with the one-time token from the emailed link. It does not sign anyone in. */
  resetPassword(email: string, token: string, newPassword: string): Observable<{ message: string }> {
    return this.http.post<{ message: string }>(`${API_URL}/auth/reset-password`, { email, token, newPassword });
  }

  /** True when a refresh token is held, so an expired login token can be renewed without the password. */
  canRefresh(): boolean {
    return !!this.session()?.refreshToken;
  }

  /**
   * Swaps the refresh token for a new login token (and a new refresh token). The login token lasts only minutes;
   * this keeps the person signed in quietly. Fails if the ticket is used up, expired or cancelled (for example the
   * password was changed), and then the person must sign in again.
   */
  refresh(): Observable<AuthResponse> {
    if (this.refreshing) {
      return this.refreshing;
    }

    const used = this.session()?.refreshToken;
    if (!used) {
      return throwError(() => new Error('There is no refresh token.'));
    }

    this.refreshing = this.http.post<AuthResponse>(`${API_URL}/auth/refresh-token`, { refreshToken: used }).pipe(
      tap((response) => this.startSession(response)),
      catchError((error: unknown) => {
        // Another tab of this browser may have used the same ticket a moment ago and saved a new one.
        // If so, carry on with that one instead of signing out.
        const saved = this.readStoredSession();
        if (saved?.refreshToken && saved.refreshToken !== used) {
          this.session.set(saved);
          return of(saved);
        }
        return throwError(() => error);
      }),
      finalize(() => (this.refreshing = null)),
      shareReplay(1),
    );

    return this.refreshing;
  }

  logout(): void {
    const refreshToken = this.session()?.refreshToken;

    this.session.set(null);
    this.storage()?.removeItem(STORAGE_KEY);

    // Cancel the refresh token on the server too, so a copy of it is worthless. If this fails, nothing is lost:
    // the person is signed out here either way.
    if (refreshToken) {
      this.http.post(`${API_URL}/auth/logout`, { refreshToken }).subscribe({ error: () => undefined });
    }
  }

  /**
   * The token to send to the API, or null if nobody is logged in or it has run out.
   * An expired token is never returned. With no refresh token to renew it, the login is cleaned up here too.
   */
  getValidToken(): string | null {
    const current = this.session();
    if (!current) {
      return null;
    }

    if (isExpired(current.token)) {
      if (!current.refreshToken) {
        this.logout();
      }
      return null; // with a refresh token, the next call renews it quietly
    }

    return current.token;
  }

  /** Signed in, even if the short-lived token has just run out but can be renewed. */
  isLoggedIn(): boolean {
    return this.getValidToken() !== null || this.canRefresh();
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
        Array.isArray(parsed.roles) &&
        (parsed.refreshToken === undefined || typeof parsed.refreshToken === 'string');

      // An expired token is fine to keep when a refresh token can renew it.
      if (!looksValid || (isExpired(parsed.token!) && !parsed.refreshToken)) {
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
  const exp = readPayload(token)?.exp;
  return typeof exp !== 'number' || exp * 1000 <= Date.now(); // an unreadable token counts as unusable
}

/** Reads the middle part of a token (the claims). It does not check the signature; the API does that. */
function readPayload(token: string): { sub?: string; exp?: number } | null {
  try {
    const payload = token.split('.')[1];
    const json = atob(payload.replace(/-/g, '+').replace(/_/g, '/'));
    return JSON.parse(json) as { sub?: string; exp?: number };
  } catch {
    return null;
  }
}
