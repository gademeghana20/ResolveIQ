import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { ResetPassword } from './reset-password';

describe('ResetPassword', () => {
  let fixture: ComponentFixture<ResetPassword>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  let snack: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(query: Record<string, string> = { email: 'dana@example.com', token: 'tok/en+1==' }) {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [ResetPassword],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(query) } } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fixture = TestBed.createComponent(ResetPassword);
    await settle();
  }

  async function submit(password: string, again = password) {
    const [first, second] = Array.from(page().querySelectorAll('input'));
    first.value = password;
    first.dispatchEvent(new Event('input'));
    second.value = again;
    second.dispatchEvent(new Event('input'));
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  it('shows two password boxes and the rules, when the link is complete', async () => {
    await setUp();

    expect(page().querySelectorAll('input[type="password"]').length).toBe(2);
    expect(page().textContent).toContain('upper-case letter');
  });

  it.each<Record<string, string>>([{}, { email: 'dana@example.com' }, { token: 'abc' }])(
    'offers a fresh link instead of a form when the link is incomplete: %j',
    async (query) => {
      await setUp(query);

      expect(page().querySelector('form')).toBeNull();
      expect(page().textContent).toContain('not valid any more');
      const link = Array.from(page().querySelectorAll('a')).find((a) => a.textContent!.includes('new link'))!;
      expect(link.getAttribute('href')).toBe('/forgot-password');
    },
  );

  it('does not save when the two boxes differ, so a typing slip cannot lock someone out', async () => {
    await setUp();

    await submit('Str0ng-Pass!', 'Str0ng-Pass?');

    http.expectNone(`${API_URL}/auth/reset-password`);
    expect(page().textContent).toContain('The two passwords do not match.');
  });

  it('sends the email, the token from the link and the new password, then goes to sign in', async () => {
    await setUp();

    await submit('Str0ng-Pass!');

    const request = http.expectOne(`${API_URL}/auth/reset-password`);
    expect(request.request.body).toEqual({ email: 'dana@example.com', token: 'tok/en+1==', newPassword: 'Str0ng-Pass!' });
    request.flush({ message: 'ok' });

    expect(navigate).toHaveBeenCalledWith('/login');
    expect(snack.mock.calls[0][0]).toContain('Your password was changed');
  });

  it('shows every password rule that was broken, and keeps the form so they can fix it', async () => {
    await setUp();
    await submit('weak');

    http.expectOne(`${API_URL}/auth/reset-password`).flush(
      { errors: { NewPassword: ['Passwords must be at least 8 characters.', 'Passwords must have at least one digit.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page().textContent).toContain('at least 8 characters');
    expect(page().textContent).toContain('at least one digit');
    expect(navigate).not.toHaveBeenCalled();
    expect(page().querySelector('form')).not.toBeNull();
  });

  it('replaces the form with a "get a new link" message when the link has been used up or has expired', async () => {
    await setUp();
    await submit('Str0ng-Pass!');

    http.expectOne(`${API_URL}/auth/reset-password`).flush(
      { errors: { Token: ['This link is not valid any more. Ask for a new one.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page().querySelector('form')).toBeNull();
    expect(page().textContent).toContain('not valid any more');
    expect(navigate).not.toHaveBeenCalled();
  });

  it('says so when the server cannot be reached', async () => {
    await setUp();
    await submit('Str0ng-Pass!');

    http.expectOne(`${API_URL}/auth/reset-password`).error(new ProgressEvent('error'));
    await settle();

    expect(page().textContent).toContain('Cannot reach the server');
  });
});
