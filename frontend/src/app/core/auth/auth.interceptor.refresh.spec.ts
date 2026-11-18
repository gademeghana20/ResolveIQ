import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, provideRouter } from '@angular/router';

import { fakeToken } from '../../testing/fake-token';
import { API_URL } from '../api-config';
import { authInterceptor } from './auth.interceptor';
import { AuthService } from './auth.service';

const unauthorised = { status: 401, statusText: 'Unauthorized' };

describe('authInterceptor: renewing an expired login quietly', () => {
  let client: HttpClient;
  let http: HttpTestingController;
  let auth: AuthService;
  let navigate: ReturnType<typeof vi.spyOn>;

  const oldToken = fakeToken(900);
  const newToken = fakeToken(1800);

  function setUp(session: { token: string; refreshToken?: string } | null = { token: oldToken, refreshToken: 'refresh-1' }) {
    localStorage.clear();
    if (session) {
      localStorage.setItem(
        'resolveiq.session',
        JSON.stringify({ ...session, email: 'dana@example.com', roles: ['Employee'] }),
      );
    }

    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideHttpClient(withInterceptors([authInterceptor])), provideHttpClientTesting()],
    });

    client = TestBed.inject(HttpClient);
    http = TestBed.inject(HttpTestingController);
    auth = TestBed.inject(AuthService);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);
    vi.spyOn(TestBed.inject(Router), 'url', 'get').mockReturnValue('/incidents/42');
  }

  const renewed = () => ({ token: newToken, refreshToken: 'refresh-2', email: 'dana@example.com', roles: ['Employee'] });

  it('on a 401, gets a new token with the refresh token and repeats the call, which then succeeds', () => {
    setUp();
    let result: unknown;

    client.get(`${API_URL}/incidents`).subscribe((r) => (result = r));
    http.expectOne(`${API_URL}/incidents`).flush({}, unauthorised);

    http.expectOne(`${API_URL}/auth/refresh-token`).flush(renewed());

    const retry = http.expectOne(`${API_URL}/incidents`);
    expect(retry.request.headers.get('Authorization')).toBe(`Bearer ${newToken}`);
    retry.flush(['the incidents']);

    expect(result).toEqual(['the incidents']);   // the screen never noticed
    expect(auth.isLoggedIn()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('works when the token had already expired in the browser: the first call goes out bare, then is renewed', () => {
    setUp({ token: fakeToken(-60), refreshToken: 'refresh-1' });

    client.get(`${API_URL}/incidents`).subscribe();
    const first = http.expectOne(`${API_URL}/incidents`);
    expect(first.request.headers.has('Authorization')).toBe(false);
    first.flush({}, unauthorised);

    http.expectOne(`${API_URL}/auth/refresh-token`).flush(renewed());
    http.expectOne(`${API_URL}/incidents`).flush([]);

    expect(auth.getValidToken()).toBe(newToken);
  });

  it('several calls rejected at once share one refresh (the ticket can be spent only once), and all are repeated', () => {
    setUp();
    const results: unknown[] = [];

    client.get(`${API_URL}/incidents`).subscribe((r) => results.push(r));
    client.get(`${API_URL}/dashboard/summary`).subscribe((r) => results.push(r));
    http.expectOne(`${API_URL}/incidents`).flush({}, unauthorised);
    http.expectOne(`${API_URL}/dashboard/summary`).flush({}, unauthorised);

    http.expectOne(`${API_URL}/auth/refresh-token`).flush(renewed());   // exactly one request

    http.expectOne(`${API_URL}/incidents`).flush('a');
    http.expectOne(`${API_URL}/dashboard/summary`).flush('b');
    expect(results.sort()).toEqual(['a', 'b']);
  });

  it('signs out and goes to login when the refresh token is refused too, and the screen still hears the 401', () => {
    setUp();
    let failedWith: number | undefined;

    client.get(`${API_URL}/incidents`).subscribe({ error: (e) => (failedWith = e.status) });
    http.expectOne(`${API_URL}/incidents`).flush({}, unauthorised);
    http.expectOne(`${API_URL}/auth/refresh-token`).flush({}, unauthorised);

    expect(failedWith).toBe(401);
    expect(auth.isLoggedIn()).toBe(false);
    expect(navigate).toHaveBeenCalledWith(['/login'], { queryParams: { returnUrl: '/incidents/42' } });
  });

  it('stays signed in when the refresh failed only because the server could not be reached', () => {
    setUp();

    client.get(`${API_URL}/incidents`).subscribe({ error: () => undefined });
    http.expectOne(`${API_URL}/incidents`).flush({}, unauthorised);
    http.expectOne(`${API_URL}/auth/refresh-token`).error(new ProgressEvent('error'));

    expect(auth.isLoggedIn()).toBe(true);
    expect(navigate).not.toHaveBeenCalled();
  });

  it('signs out if the repeated call is refused again, rather than looping', () => {
    setUp();
    let failedWith: number | undefined;

    client.get(`${API_URL}/incidents`).subscribe({ error: (e) => (failedWith = e.status) });
    http.expectOne(`${API_URL}/incidents`).flush({}, unauthorised);
    http.expectOne(`${API_URL}/auth/refresh-token`).flush(renewed());
    http.expectOne(`${API_URL}/incidents`).flush({}, unauthorised);   // refused again

    http.expectNone(`${API_URL}/auth/refresh-token`);                 // no second refresh
    expect(failedWith).toBe(401);
    expect(navigate).toHaveBeenCalled();
  });

  it('does not try to refresh after a 403 or a server error', () => {
    setUp();

    client.get(`${API_URL}/users`).subscribe({ error: () => undefined });
    http.expectOne(`${API_URL}/users`).flush({}, { status: 403, statusText: 'Forbidden' });
    client.get(`${API_URL}/incidents`).subscribe({ error: () => undefined });
    http.expectOne(`${API_URL}/incidents`).flush({}, { status: 500, statusText: 'Server Error' });

    http.expectNone(`${API_URL}/auth/refresh-token`);
    expect(auth.isLoggedIn()).toBe(true);
  });

  it('a session without a refresh token behaves as before: a 401 signs out at once', () => {
    setUp({ token: oldToken });

    client.get(`${API_URL}/incidents`).subscribe({ error: () => undefined });
    http.expectOne(`${API_URL}/incidents`).flush({}, unauthorised);

    http.expectNone(`${API_URL}/auth/refresh-token`);
    expect(auth.isLoggedIn()).toBe(false);
    expect(navigate).toHaveBeenCalled();
  });

  describe('which auth calls carry the token', () => {
    it.each(['login', 'register', 'registration', 'forgot-password', 'reset-password', 'refresh-token', 'logout'])(
      '%s is sent without one, and a 401 from it never signs anyone out',
      (name) => {
        setUp();

        client.post(`${API_URL}/auth/${name}`, {}).subscribe({ error: () => undefined });
        const request = http.expectOne(`${API_URL}/auth/${name}`);
        expect(request.request.headers.has('Authorization')).toBe(false);
        request.flush({}, unauthorised);

        http.expectNone(`${API_URL}/auth/refresh-token`);
        expect(auth.isLoggedIn()).toBe(true);
        expect(navigate).not.toHaveBeenCalled();
      },
    );

    it('change-password does carry the token, because it needs to know who you are', () => {
      setUp();

      client.post(`${API_URL}/auth/change-password`, {}).subscribe();

      const request = http.expectOne(`${API_URL}/auth/change-password`);
      expect(request.request.headers.get('Authorization')).toBe(`Bearer ${oldToken}`);
      request.flush({});
    });
  });
});
