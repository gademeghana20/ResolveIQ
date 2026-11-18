import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { fakeToken } from '../../../testing/fake-token';
import { Login } from './login';

describe('Login', () => {
  let fixture: ComponentFixture<Login>;
  let http: HttpTestingController;
  let router: Router;
  let navigate: ReturnType<typeof vi.spyOn>;

  async function setUp(returnUrl: string | null = null, reason: string | null = null) {
    localStorage.clear();

    await TestBed.configureTestingModule({
      imports: [Login],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({ ...(returnUrl ? { returnUrl } : {}), ...(reason ? { reason } : {}) }) } },
        },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    router = TestBed.inject(Router);
    navigate = vi.spyOn(router, 'navigateByUrl').mockResolvedValue(true);
    fixture = TestBed.createComponent(Login);
    await fixture.whenStable();
  }

  const page = () => fixture.nativeElement as HTMLElement;

  async function fillAndSubmit(email: string, password: string) {
    const [emailInput, passwordInput] = Array.from(page().querySelectorAll('input'));
    emailInput.value = email;
    emailInput.dispatchEvent(new Event('input'));
    passwordInput.value = password;
    passwordInput.dispatchEvent(new Event('input'));

    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await fixture.whenStable();
  }

  it('explains why the person is back here when their session ended', async () => {
    await setUp('/incidents/42', 'session-ended');

    expect(page().textContent).toContain('Your session ended');
    expect(page().querySelector('[role="status"]')).not.toBeNull();
  });

  it('says nothing about a session when the person simply arrived', async () => {
    await setUp();

    expect(page().textContent).not.toContain('Your session ended');
  });

  it('shows the email and password fields and a sign-in button', async () => {
    await setUp();

    expect(page().querySelectorAll('input').length).toBe(2);
    expect(page().querySelector('button')?.textContent).toContain('Sign in');
  });

  it('has just the heading "Welcome back", with no extra tagline under it', async () => {
    await setUp();

    expect(page().textContent).toContain('Welcome back');
    expect(page().textContent).not.toContain('Sign in to manage incidents');
    expect(page().querySelector('mat-card-subtitle')).toBeNull();
  });

  it('offers a way to create an account', async () => {
    await setUp();

    const link = Array.from(page().querySelectorAll('a')).find((a) => a.textContent!.includes('Create an account'))!;
    expect(link.getAttribute('href')).toBe('/register');
  });

  it('offers a way to recover a forgotten password', async () => {
    await setUp();

    const link = Array.from(page().querySelectorAll('a')).find((a) => a.textContent!.includes('Forgot your password'))!;
    expect(link.getAttribute('href')).toBe('/forgot-password');
  });

  it('keeps the Create an account link when the server says registration is open', async () => {
    await setUp();
    http.expectOne(`${API_URL}/auth/registration`).flush({ enabled: true, allowedEmailDomains: [] });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(Array.from(page().querySelectorAll('a')).some((a) => a.textContent!.includes('Create an account'))).toBe(true);
  });

  it('hides the Create an account link when registration is closed', async () => {
    await setUp();
    http.expectOne(`${API_URL}/auth/registration`).flush({ enabled: false, allowedEmailDomains: [] });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(Array.from(page().querySelectorAll('a')).some((a) => a.textContent!.includes('Create an account'))).toBe(false);
  });

  it('keeps the link if the server cannot be asked, rather than hiding the way to sign up', async () => {
    await setUp();
    http.expectOne(`${API_URL}/auth/registration`).error(new ProgressEvent('error'));
    await fixture.whenStable();
    fixture.detectChanges();

    expect(Array.from(page().querySelectorAll('a')).some((a) => a.textContent!.includes('Create an account'))).toBe(true);
  });

  it('says to wait a minute when sign-in is being tried too fast', async () => {
    await setUp();

    await fillAndSubmit('manager@example.com', 'secret');
    http.expectOne(`${API_URL}/auth/login`).flush({}, { status: 429, statusText: 'Too Many Requests' });
    await fixture.whenStable();
    fixture.detectChanges();

    expect(page().textContent).toContain('Too many attempts. Please wait a minute');
  });

  it('does not call the API when the form is empty, and says what is missing', async () => {
    await setUp();

    await fillAndSubmit('', '');

    http.expectNone(`${API_URL}/auth/login`);
    expect(page().textContent).toContain('Email is required.');
    expect(page().textContent).toContain('Password is required.');
  });

  it('rejects an email that is not an email, without calling the API', async () => {
    await setUp();

    await fillAndSubmit('not-an-email', 'secret');

    http.expectNone(`${API_URL}/auth/login`);
    expect(page().textContent).toContain('Enter a valid email address.');
  });

  it('after a correct login, goes to the incidents page', async () => {
    await setUp();

    await fillAndSubmit('manager@example.com', 'secret');
    http.expectOne(`${API_URL}/auth/login`).flush({ token: fakeToken(), email: 'manager@example.com', roles: ['Manager'] });

    expect(navigate).toHaveBeenCalledWith('/incidents');
  });

  it('after login, returns to the page the person originally asked for', async () => {
    await setUp('/incidents/123');

    await fillAndSubmit('manager@example.com', 'secret');
    http.expectOne(`${API_URL}/auth/login`).flush({ token: fakeToken(), email: 'manager@example.com', roles: ['Manager'] });

    expect(navigate).toHaveBeenCalledWith('/incidents/123');
  });

  it.each(['https://evil.example.com', '//evil.example.com', 'javascript:alert(1)'])(
    'ignores an outside address in returnUrl (%s)',
    async (evil) => {
      await setUp(evil);

      await fillAndSubmit('manager@example.com', 'secret');
      http.expectOne(`${API_URL}/auth/login`).flush({ token: fakeToken(), email: 'manager@example.com', roles: ['Manager'] });

      expect(navigate).toHaveBeenCalledWith('/incidents');
    },
  );

  it('shows a clear message for a wrong password and stays on the page', async () => {
    await setUp();

    await fillAndSubmit('manager@example.com', 'wrong');
    http.expectOne(`${API_URL}/auth/login`).flush(
      { message: 'Invalid email or password.' },
      { status: 401, statusText: 'Unauthorized' },
    );
    await fixture.whenStable();

    expect(page().textContent).toContain('Invalid email or password.');
    expect(navigate).not.toHaveBeenCalled();
    expect(page().querySelector('button')?.disabled).toBe(false); // can try again
  });

  it('says so when the API cannot be reached', async () => {
    await setUp();

    await fillAndSubmit('manager@example.com', 'secret');
    http.expectOne(`${API_URL}/auth/login`).error(new ProgressEvent('error'));
    await fixture.whenStable();

    expect(page().textContent).toContain('Cannot reach the server');
  });
});
