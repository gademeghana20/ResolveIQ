import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { fakeToken } from '../../testing/fake-token';
import { API_URL } from '../api-config';
import { AuthResponse } from './auth.models';
import { AuthService } from './auth.service';

const STORAGE_KEY = 'resolveiq.session';

function session(overrides: Partial<AuthResponse> = {}): AuthResponse {
  return { token: fakeToken(900), refreshToken: 'refresh-1', email: 'dana@example.com', roles: ['Employee'], ...overrides };
}

describe('AuthService: staying signed in with a refresh token', () => {
  let http: HttpTestingController;

  function createService(saved?: AuthResponse): AuthService {
    localStorage.clear();
    if (saved) {
      localStorage.setItem(STORAGE_KEY, JSON.stringify(saved));
    }
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(AuthService);
  }

  it('keeps the refresh token it was given at sign-in, and saves it for the next page load', () => {
    const auth = createService();

    auth.login({ email: 'dana@example.com', password: 'x' }).subscribe();
    http.expectOne(`${API_URL}/auth/login`).flush(session());

    expect(auth.canRefresh()).toBe(true);
    expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!).refreshToken).toBe('refresh-1');
  });

  it('counts an expired login token as still signed in when it can be renewed, and does not sign out', () => {
    const auth = createService(session({ token: fakeToken(-60) }));

    expect(auth.getValidToken()).toBeNull();     // never hands out an expired token...
    expect(auth.isLoggedIn()).toBe(true);        // ...but the person is still signed in
    expect(auth.email()).toBe('dana@example.com');
    expect(localStorage.getItem(STORAGE_KEY)).not.toBeNull();
  });

  it('still drops an expired login that has no refresh token (a session saved by an older version)', () => {
    const auth = createService(session({ token: fakeToken(-60), refreshToken: undefined }));

    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
  });

  it('ignores a saved session whose refresh token is not text', () => {
    localStorage.clear();
    localStorage.setItem(STORAGE_KEY, JSON.stringify({ ...session(), refreshToken: 12345 }));
    TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });

    expect(TestBed.inject(AuthService).isLoggedIn()).toBe(false);
  });

  describe('refresh()', () => {
    it('sends the refresh token, then replaces both tokens with the new ones, saved for the next page load', () => {
      const auth = createService(session({ token: fakeToken(-60) }));
      const renewed = session({ token: fakeToken(900), refreshToken: 'refresh-2' });

      auth.refresh().subscribe();
      const request = http.expectOne(`${API_URL}/auth/refresh-token`);
      expect(request.request.body).toEqual({ refreshToken: 'refresh-1' });
      request.flush(renewed);

      expect(auth.getValidToken()).toBe(renewed.token);
      expect(JSON.parse(localStorage.getItem(STORAGE_KEY)!).refreshToken).toBe('refresh-2');
    });

    it('shares one request between callers, because the ticket can be spent only once', () => {
      const auth = createService(session());
      let first: AuthResponse | undefined;
      let second: AuthResponse | undefined;

      auth.refresh().subscribe((r) => (first = r));
      auth.refresh().subscribe((r) => (second = r));
      http.expectOne(`${API_URL}/auth/refresh-token`).flush(session({ refreshToken: 'refresh-2' }));

      expect(first?.refreshToken).toBe('refresh-2');
      expect(second?.refreshToken).toBe('refresh-2');
    });

    it('can run again later, once the earlier refresh has finished', () => {
      const auth = createService(session());

      auth.refresh().subscribe();
      http.expectOne(`${API_URL}/auth/refresh-token`).flush(session({ refreshToken: 'refresh-2' }));

      auth.refresh().subscribe();
      const again = http.expectOne(`${API_URL}/auth/refresh-token`);
      expect(again.request.body).toEqual({ refreshToken: 'refresh-2' });
      again.flush(session({ refreshToken: 'refresh-3' }));
    });

    it('reports a refusal, and leaves the saved session for the caller to deal with', () => {
      const auth = createService(session());
      let failedWith: number | undefined;

      auth.refresh().subscribe({ error: (e) => (failedWith = e.status) });
      http.expectOne(`${API_URL}/auth/refresh-token`).flush({}, { status: 401, statusText: 'Unauthorized' });

      expect(failedWith).toBe(401);
    });

    it('joins another tab that has just refreshed with the same ticket, instead of failing', () => {
      const auth = createService(session());
      let result: AuthResponse | undefined;

      auth.refresh().subscribe((r) => (result = r));
      // Meanwhile another tab used the ticket first and saved its replacement.
      localStorage.setItem(STORAGE_KEY, JSON.stringify(session({ token: fakeToken(900), refreshToken: 'from-other-tab' })));
      http.expectOne(`${API_URL}/auth/refresh-token`).flush({}, { status: 401, statusText: 'Unauthorized' });

      expect(result?.refreshToken).toBe('from-other-tab');
      expect(auth.canRefresh()).toBe(true);
    });

    it('fails clearly when there is no refresh token to use', () => {
      const auth = createService();
      let failed = false;

      auth.refresh().subscribe({ error: () => (failed = true) });

      expect(failed).toBe(true);
      http.expectNone(`${API_URL}/auth/refresh-token`);
    });
  });

  describe('logout()', () => {
    it('cancels the refresh token on the server too, so a copy of it is useless', () => {
      const auth = createService(session());

      auth.logout();

      const request = http.expectOne(`${API_URL}/auth/logout`);
      expect(request.request.body).toEqual({ refreshToken: 'refresh-1' });
      request.flush(null, { status: 204, statusText: 'No Content' });
      expect(auth.isLoggedIn()).toBe(false);
      expect(localStorage.getItem(STORAGE_KEY)).toBeNull();
    });

    it('signs out here even if the server cannot be reached', () => {
      const auth = createService(session());

      auth.logout();
      http.expectOne(`${API_URL}/auth/logout`).error(new ProgressEvent('error'));

      expect(auth.isLoggedIn()).toBe(false);
    });

    it('makes no call when there was no refresh token', () => {
      const auth = createService(session({ refreshToken: undefined }));

      auth.logout();

      http.expectNone(`${API_URL}/auth/logout`);
      expect(auth.isLoggedIn()).toBe(false);
    });
  });
});
