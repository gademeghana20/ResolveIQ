import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { Router, provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { AuthService } from '../../../core/auth/auth.service';
import { fakeToken } from '../../../testing/fake-token';
import { Register } from './register';

const URL = `${API_URL}/auth/register`;
const GOOD_PASSWORD = 'Str0ng-Pass!';

describe('Register', () => {
  let fixture: ComponentFixture<Register>;
  let http: HttpTestingController;
  let navigate: ReturnType<typeof vi.spyOn>;
  let snack: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const submitButton = () => page().querySelector('button[type="submit"]') as HTMLButtonElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function fill(email: string, password: string, repeat = password) {
    const [emailBox, passwordBox, repeatBox] = Array.from(page().querySelectorAll('input')) as HTMLInputElement[];
    for (const [box, value] of [[emailBox, email], [passwordBox, password], [repeatBox, repeat]] as const) {
      box.value = value;
      box.dispatchEvent(new Event('input'));
      box.dispatchEvent(new Event('blur'));
    }
    await settle();
  }

  async function submit() {
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [Register],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fixture = TestBed.createComponent(Register);
    await settle();
  });

  describe('the page', () => {
    it('asks for an email, a password and the password again', () => {
      expect(page().querySelectorAll('input').length).toBe(3);
      expect(text()).toContain('Email');
      expect(text()).toContain('Type the password again');
    });

    it('tells people the password rules before they type', () => {
      expect(text()).toContain('upper-case letter');
      expect(text()).toContain('digit');
    });

    it('says new accounts are Employees, and warns not to reuse a password', () => {
      expect(text()).toContain('start as Employees');
      expect(text()).toContain('do not use a password you use anywhere else');
    });

    it('lets the password rules take as many lines as they need, instead of spilling onto the next box', () => {
      const passwordField = Array.from(page().querySelectorAll('mat-form-field')).find((field) =>
        field.textContent!.includes('upper-case letter'),
      )!;

      // "dynamic" sizing makes the strip under the box grow with its text (a fixed strip overlapped the next box).
      expect(passwordField.querySelector('.mat-mdc-form-field-subscript-wrapper')!.classList).toContain(
        'mat-mdc-form-field-subscript-dynamic-size',
      );
    });

    it('does the same for the email box, which can show several complaints', () => {
      const emailField = Array.from(page().querySelectorAll('mat-form-field')).find((field) =>
        field.textContent!.includes('Email'),
      )!;

      expect(emailField.querySelector('.mat-mdc-form-field-subscript-wrapper')!.classList).toContain(
        'mat-mdc-form-field-subscript-dynamic-size',
      );
    });

    it('links back to the sign-in page', () => {
      const link = Array.from(page().querySelectorAll('a')).find((a) => a.textContent!.includes('Sign in'))!;

      expect(link.getAttribute('href')).toBe('/login');
    });
  });

  describe('checks before sending anything', () => {
    it('does not send an empty form, and says what is missing', async () => {
      await submit();

      http.expectNone(URL);
      expect(text()).toContain('Email is required.');
      expect(text()).toContain('A password is required.');
    });

    it('rejects something that is not an email', async () => {
      await fill('not-an-email', GOOD_PASSWORD);
      await submit();

      http.expectNone(URL);
      expect(text()).toContain('Enter a full email address, like name@example.com.');
    });

    it.each(['test1@example', 'sam@gmail', 'name@example.', 'name@example.c'])(
      'rejects %s, an address without a full domain, and sends nothing',
      async (email) => {
        await fill(email, GOOD_PASSWORD);
        await submit();

        http.expectNone(URL);
        expect(text()).toContain('like name@example.com');
      },
    );

    it('accepts a full address', async () => {
      await fill('name@example.com', GOOD_PASSWORD);
      await submit();

      http.expectOne(URL).flush({ token: fakeToken(), email: 'name@example.com', roles: ['Employee'] });
    });

    it('does not send when the two passwords differ, so a typing slip cannot lock someone out', async () => {
      await fill('new@example.com', GOOD_PASSWORD, 'Str0ng-Pasx!');
      await submit();

      http.expectNone(URL);
      expect(text()).toContain('The two passwords do not match.');
    });
  });

  describe('creating the account', () => {
    it('sends the email and password (not the repeat), and signs the person in', async () => {
      await fill('  new@example.com  ', GOOD_PASSWORD);
      await submit();

      const request = http.expectOne(URL);
      expect(request.request.method).toBe('POST');
      expect(request.request.body).toEqual({ email: 'new@example.com', password: GOOD_PASSWORD });
      request.flush({ token: fakeToken(), email: 'new@example.com', roles: ['Employee'] });
      await settle();

      expect(TestBed.inject(AuthService).isLoggedIn()).toBe(true);
      expect(TestBed.inject(AuthService).roles()).toEqual(['Employee']);
    });

    it('welcomes the person and opens the incident list', async () => {
      await fill('new@example.com', GOOD_PASSWORD);
      await submit();

      http.expectOne(URL).flush({ token: fakeToken(), email: 'new@example.com', roles: ['Employee'] });
      await settle();

      expect(snack.mock.calls[0][0]).toContain('Welcome');
      expect(navigate).toHaveBeenCalledWith('/incidents');
    });

    it('cannot be submitted twice while saving', async () => {
      await fill('new@example.com', GOOD_PASSWORD);
      await submit();

      expect(submitButton().disabled).toBe(true);
      expect(submitButton().textContent).toContain('Creating account');
      http.expectOne(URL).flush({ token: fakeToken(), email: 'new@example.com', roles: ['Employee'] });
    });
  });

  describe('when the API refuses', () => {
    async function refuse(errors: object, typed = ['new@example.com', GOOD_PASSWORD]) {
      await fill(typed[0], typed[1]);
      await submit();
      http.expectOne(URL).flush({ errors }, { status: 400, statusText: 'Bad Request' });
      await settle();
    }

    it('says an email is already taken, next to the email, and stays on the page', async () => {
      await refuse({ DuplicateUserName: ["Username 'taken@example.com' is already taken."] }, ['taken@example.com', GOOD_PASSWORD]);

      expect(text()).toContain("Username 'taken@example.com' is already taken.");
      expect(navigate).not.toHaveBeenCalled();
      expect(submitButton().disabled).toBe(false);
    });

    it('shows both messages when Identity reports the email twice', async () => {
      await refuse({
        DuplicateUserName: ["Username 'a@example.com' is already taken."],
        DuplicateEmail: ["Email 'a@example.com' is already taken."],
      });

      expect(text()).toContain("Username 'a@example.com' is already taken.");
      expect(text()).toContain("Email 'a@example.com' is already taken.");
    });

    it('lists every password rule that was broken, all at once', async () => {
      await refuse(
        {
          PasswordTooShort: ['Passwords must be at least 8 characters.'],
          PasswordRequiresUpper: ["Passwords must have at least one uppercase ('A'-'Z')."],
          PasswordRequiresDigit: ["Passwords must have at least one digit ('0'-'9')."],
        },
        ['new@example.com', 'short'],
      );

      expect(text()).toContain('Passwords must be at least 8 characters.');
      expect(text()).toContain("Passwords must have at least one uppercase ('A'-'Z').");
      expect(text()).toContain("Passwords must have at least one digit ('0'-'9').");
    });

    it('puts a plain "invalid email" complaint next to the email', async () => {
      await refuse({ Email: ['The Email field is not a valid e-mail address.'] });

      expect(text()).toContain('The Email field is not a valid e-mail address.');
    });

    it('keeps what the person typed', async () => {
      await refuse({ PasswordTooShort: ['Passwords must be at least 8 characters.'] }, ['new@example.com', GOOD_PASSWORD]);

      const [emailBox] = Array.from(page().querySelectorAll('input')) as HTMLInputElement[];
      expect(emailBox.value).toBe('new@example.com');
    });

    it('shows a complaint about anything else in a general message', async () => {
      await refuse({ Something: ['An unexpected rule was broken.'] });

      expect(text()).toContain('An unexpected rule was broken.');
    });

    it('says so when the server cannot be reached', async () => {
      await fill('new@example.com', GOOD_PASSWORD);
      await submit();

      http.expectOne(URL).error(new ProgressEvent('error'));
      await settle();

      expect(text()).toContain('Cannot reach the server');
      expect(navigate).not.toHaveBeenCalled();
    });

    it('gives a general message for a server error', async () => {
      await fill('new@example.com', GOOD_PASSWORD);
      await submit();

      http.expectOne(URL).flush({}, { status: 500, statusText: 'Server Error' });
      await settle();

      expect(text()).toContain('could not be created');
    });
  });
});
