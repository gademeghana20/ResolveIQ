import { HttpClient } from '@angular/common/http';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { fakeToken } from '../../testing/fake-token';
import { API_URL } from '../api-config';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

describe('authInterceptor', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;
  let navigate: ReturnType<typeof vi.spyOn>;

  const token = fakeToken();

  function setUp(loggedIn: boolean) {
    localStorage.clear();
    if (loggedIn) {
      localStorage.setItem('resolveiq.session', JSON.stringify({ token, email: 'a@example.com', roles: ['Employee'] }));
    }

    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor])),
        provideHttpClientTesting(),
      ],
    });

    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
  }

  it('adds the token to calls to our API when logged in', () => {
    setUp(true);

    client.get(`${API_URL}/incidents`).subscribe();

    const request = http.expectOne(`${API_URL}/incidents`);
    expect(request.request.headers.get('Authorization')).toBe(`Bearer ${token}`);
    request.flush([]);
  });

  it('sends no Authorization header when logged out', () => {
    setUp(false);

    client.get(`${API_URL}/categories`).subscribe();

    const request = http.expectOne(`${API_URL}/categories`);
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush([]);
  });

  it('never sends the token to any other website', () => {
    setUp(true);

    client.get('https://other-site.example.com/data').subscribe();

    const request = http.expectOne('https://other-site.example.com/data');
    expect(request.request.headers.has('Authorization')).toBe(false);
    request.flush({});
  });

  it('a 401 on a call that carried a token signs you out and sends you to login', () => {
    setUp(true);
    vi.spyOn(TestBed.inject(Router), 'url', 'get').mockReturnValue('/incidents/42');
    let failedWith: number | undefined;

    client.get(`${API_URL}/incidents`).subscribe({ error: (e) => (failedWith = e.status) });
    http.expectOne(`${API_URL}/incidents`).flush({}, { status: 401, statusText: 'Unauthorized' });

    expect(failedWith).toBe(401); // the screen still hears about the error
    expect(auth.isLoggedIn()).toBe(false);
    expect(localStorage.getItem('resolveiq.session')).toBeNull();
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/incidents/42', reason: 'session-ended' } });
  });

  it('a 401 from the login call itself (wrong password) does not redirect anywhere', () => {
    setUp(false);
    let failedWith: number | undefined;

    client.post(`${API_URL}/auth/login`, {}).subscribe({ error: (e) => (failedWith = e.status) });
    http.expectOne(`${API_URL}/auth/login`).flush({ message: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });

    expect(failedWith).toBe(401);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('a wrong password does not sign out or redirect, even if an old session is still saved', () => {
    setUp(true); // someone is still "logged in" in this browser
    let failedWith: number | undefined;

    client.post(`${API_URL}/auth/login`, {}).subscribe({ error: (e) => (failedWith = e.status) });
    const request = http.expectOne(`${API_URL}/auth/login`);
    expect(request.request.headers.has('Authorization')).toBe(false); // no token goes to the login call
    request.flush({ message: 'Invalid email or password.' }, { status: 401, statusText: 'Unauthorized' });

    expect(failedWith).toBe(401);
    expect(auth.isLoggedIn()).toBe(true); // the saved session is untouched
    expect(navigate).not.toHaveBeenCalled();
  });

  it('a 403 (not allowed) keeps you logged in', () => {
    setUp(true);

    client.get(`${API_URL}/users`).subscribe({ error: () => undefined });
    http.expectOne(`${API_URL}/users`).flush({}, { status: 403, statusText: 'Forbidden' });

    expect(auth.isLoggedIn()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('other errors such as a server failure keep you logged in too', () => {
    setUp(true);

    client.get(`${API_URL}/incidents`).subscribe({ error: () => undefined });
    http.expectOne(`${API_URL}/incidents`).flush({}, { status: 500, statusText: 'Server Error' });

    expect(auth.isLoggedIn()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });
});
