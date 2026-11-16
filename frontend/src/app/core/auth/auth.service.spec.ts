import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';

import { API_URL } from '../api-config';
import { AuthResponse } from './auth.models';
import { fakeToken } from '../../testing/fake-token';
import { AuthService } from './auth.service';

function response(overrides: Partial<AuthResponse> = {}): AuthResponse {
  return { token: fakeToken(3600), email: 'manager@example.com', roles: ['Manager'], ...overrides };
}

describe('AuthService', () => {
  let http: HttpTestingController;

  function createService(): AuthService {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    http = TestBed.inject(HttpTestingController);
    return TestBed.inject(AuthService);
  }

  beforeEach(() => localStorage.clear());

  it('starts logged out', () => {
    const auth = createService();

    expect(auth.isLoggedIn()).toBe(false);
    expect(auth.getValidToken()).toBeNull();
    expect(auth.email()).toBeNull();
    expect(auth.roles()).toEqual([]);
  });

  it('login posts the credentials to the API and then knows who is logged in', () => {
    const auth = createService();
    const reply = response();

    auth.login({ email: 'manager@example.com', password: 'secret' }).subscribe();

    const request = http.expectOne(`${API_URL}/auth/login`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'manager@example.com', password: 'secret' });
    request.flush(reply);

    expect(auth.isLoggedIn()).toBe(true);
    expect(auth.getValidToken()).toBe(reply.token);
    expect(auth.email()).toBe('manager@example.com');
    expect(auth.roles()).toEqual(['Manager']);
  });

  it('a failed login leaves nobody logged in', () => {
    const auth = createService();
    let failedWith: number | undefined;

    auth.login({ email: 'a@example.com', password: 'wrong' }).subscribe({
      error: (error) => (failedWith = error.status),
    });
    http.expectOne(`${API_URL}/auth/login`).flush(
      { message: 'Invalid email or password.' },
      { status: 401, statusText: 'Unauthorized' },
    );

    expect(failedWith).toBe(401);
    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem('resolveiq.session')).toBeNull();
  });

  it('register logs the new person in too', () => {
    const auth = createService();

    auth.register({ email: 'new@example.com', password: 'Password123!' }).subscribe();
    http
      .expectOne(`${API_URL}/auth/register`)
      .flush(response({ email: 'new@example.com', roles: ['Employee'] }));

    expect(auth.email()).toBe('new@example.com');
    expect(auth.roles()).toEqual(['Employee']);
  });

  it('logout forgets everything, including what was saved in the browser', () => {
    const auth = createService();
    auth.login({ email: 'x@example.com', password: 'p' }).subscribe();
    http.expectOne(`${API_URL}/auth/login`).flush(response());

    auth.logout();

    expect(auth.isLoggedIn()).toBe(false);
    expect(auth.email()).toBeNull();
    expect(localStorage.getItem('resolveiq.session')).toBeNull();
  });

  it('keeps you logged in after a page refresh', () => {
    localStorage.setItem('resolveiq.session', JSON.stringify(response({ email: 'kept@example.com' })));

    const auth = createService();

    expect(auth.isLoggedIn()).toBe(true);
    expect(auth.email()).toBe('kept@example.com');
  });

  it('does not restore a saved login whose token has expired', () => {
    localStorage.setItem('resolveiq.session', JSON.stringify(response({ token: fakeToken(-60) })));

    const auth = createService();

    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem('resolveiq.session')).toBeNull();
  });

  it('ignores a damaged saved login instead of crashing', () => {
    localStorage.setItem('resolveiq.session', '{ not json');
    expect(createService().isLoggedIn()).toBe(false);
  });

  it('ignores a saved login with the wrong shape', () => {
    localStorage.setItem('resolveiq.session', JSON.stringify({ token: 123 }));
    expect(createService().isLoggedIn()).toBe(false);
  });

  it('stops using a token once it has run out', () => {
    const auth = createService();
    auth.login({ email: 'x@example.com', password: 'p' }).subscribe();
    http.expectOne(`${API_URL}/auth/login`).flush(response({ token: fakeToken(-1) }));

    expect(auth.getValidToken()).toBeNull();
    expect(auth.isLoggedIn()).toBe(false);
    expect(auth.email()).toBeNull(); // it logged out as well
  });

  it('hasRole is true only for the roles the person has', () => {
    const auth = createService();
    auth.login({ email: 'x@example.com', password: 'p' }).subscribe();
    http.expectOne(`${API_URL}/auth/login`).flush(response({ roles: ['Manager'] }));

    expect(auth.hasRole('Manager')).toBe(true);
    expect(auth.hasRole('Admin', 'Manager')).toBe(true);
    expect(auth.hasRole('Admin')).toBe(false);
  });

  it('hasRole is false when logged out', () => {
    expect(createService().hasRole('Admin', 'Manager', 'Employee')).toBe(false);
  });
});
