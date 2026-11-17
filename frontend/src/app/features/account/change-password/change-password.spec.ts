import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';

import { API_URL } from '../../../core/api-config';
import { AuthService } from '../../../core/auth/auth.service';
import { fakeToken } from '../../../testing/fake-token';
import { ChangePassword } from './change-password';

describe('ChangePassword', () => {
  let fixture: ComponentFixture<ChangePassword>;
  let http: HttpTestingController;
  let snack: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function submit(current: string, next: string, again = next) {
    const [a, b, c] = Array.from(page().querySelectorAll('input'));
    for (const [input, value] of [[a, current], [b, next], [c, again]] as const) {
      input.value = value;
      input.dispatchEvent(new Event('input'));
    }
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  beforeEach(async () => {
    localStorage.clear();
    localStorage.setItem(
      'resolveiq.session',
      JSON.stringify({ token: fakeToken(), email: 'dana@example.com', roles: ['Employee'] }),
    );

    await TestBed.configureTestingModule({
      imports: [ChangePassword],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fixture = TestBed.createComponent(ChangePassword);
    await settle();
  });

  it('asks for the current password and the new one twice, and lists the rules', () => {
    expect(page().querySelectorAll('input[type="password"]').length).toBe(3);
    expect(page().textContent).toContain('upper-case letter');
  });

  it('does not save empty boxes, or two new passwords that differ', async () => {
    await submit('', '', '');
    expect(page().textContent).toContain('Type your current password.');

    await submit('Old-Pass1!', 'Str0ng-Pass!', 'Str0ng-Pass?');
    expect(page().textContent).toContain('The two passwords do not match.');

    http.expectNone(`${API_URL}/auth/change-password`);
  });

  it('sends both passwords, keeps the person signed in with the fresh login, and clears the boxes', async () => {
    const auth = TestBed.inject(AuthService);
    await submit('Old-Pass1!', 'Str0ng-Pass!');

    const request = http.expectOne(`${API_URL}/auth/change-password`);
    expect(request.request.body).toEqual({ currentPassword: 'Old-Pass1!', newPassword: 'Str0ng-Pass!' });

    const fresh = fakeToken(7200);
    request.flush({ token: fresh, email: 'dana@example.com', roles: ['Employee'] });
    await settle();

    expect(auth.getValidToken()).toBe(fresh); // not thrown out of the app
    expect(snack.mock.calls[0][0]).toContain('Your password was changed');
    expect((page().querySelector('input') as HTMLInputElement).value).toBe('');
  });

  it('puts a wrong-current-password complaint beside that box, and keeps what was typed in the others', async () => {
    await submit('Wrong-Pass1!', 'Str0ng-Pass!');

    http.expectOne(`${API_URL}/auth/change-password`).flush(
      { errors: { CurrentPassword: ['The current password is not correct.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page().textContent).toContain('The current password is not correct.');
    expect((page().querySelectorAll('input')[1] as HTMLInputElement).value).toBe('Str0ng-Pass!');
    expect(snack).not.toHaveBeenCalled();
  });

  it('shows every rule a weak new password broke', async () => {
    await submit('Old-Pass1!', 'weak');

    http.expectOne(`${API_URL}/auth/change-password`).flush(
      { errors: { NewPassword: ['Passwords must be at least 8 characters.', 'Passwords must have at least one digit.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page().textContent).toContain('at least 8 characters');
    expect(page().textContent).toContain('at least one digit');
  });

  it('cannot be submitted twice while saving', async () => {
    await submit('Old-Pass1!', 'Str0ng-Pass!');

    const button = page().querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(button.disabled).toBe(true);
    http.expectOne(`${API_URL}/auth/change-password`).flush({ token: fakeToken(), email: 'dana@example.com', roles: [] });
  });

  it('says so when the server cannot be reached', async () => {
    await submit('Old-Pass1!', 'Str0ng-Pass!');

    http.expectOne(`${API_URL}/auth/change-password`).error(new ProgressEvent('error'));
    await settle();

    expect(page().textContent).toContain('Cannot reach the server');
  });
});
