import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { API_URL } from '../../../core/api-config';
import { UserSummary } from '../../../core/user.models';
import { ChangeRoleDialog } from './change-role-dialog';
import { CreateUserDialog } from './create-user-dialog';
import { ResetPasswordDialog } from './reset-password-dialog';

const created: UserSummary = { id: 'new-1', email: 'new@example.com', roles: ['Manager'], isActive: true };

async function configure(component: unknown, data?: unknown) {
  const close = vi.fn();

  await TestBed.configureTestingModule({
    imports: [component as never],
    providers: [
      provideHttpClient(),
      provideHttpClientTesting(),
      { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: { close } },
    ],
  }).compileComponents();

  return { close, http: TestBed.inject(HttpTestingController) };
}

function typeInto(root: HTMLElement, selector: string, value: string) {
  const field = root.querySelector(selector) as HTMLInputElement;
  field.value = value;
  field.dispatchEvent(new Event('input'));
}

describe('CreateUserDialog', () => {
  let fixture: ComponentFixture<CreateUserDialog>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const form = () => (fixture.componentInstance as unknown as { form: CreateUserDialog['form'] }).form;
  const submitButton = () => page().querySelector('button[type="submit"]') as HTMLButtonElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function submit() {
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  async function fill(email = 'new@example.com', password = 'Str0ng-Pass!') {
    typeInto(page(), 'input[type="email"]', email);
    typeInto(page(), 'input[type="password"]', password);
    await settle();
  }

  beforeEach(async () => {
    ({ http, close } = await configure(CreateUserDialog));
    fixture = TestBed.createComponent(CreateUserDialog);
    await settle();
  });

  it('starts with the Employee role, the safest choice', () => {
    expect(form().controls.role.value).toBe('Employee');
  });

  it('does not save an empty form, and says what is missing', async () => {
    await submit();

    http.expectNone(`${API_URL}/users`);
    expect(page().textContent).toContain('An email is required.');
    expect(page().textContent).toContain('A password is required.');
  });

  it('rejects something that is not an email', async () => {
    await fill('not-an-email');
    await submit();

    http.expectNone(`${API_URL}/users`);
    expect(page().textContent).toContain('Enter a full email address, like name@example.com.');
  });

  it('rejects an address without a full domain, which the API would refuse too', async () => {
    await fill('test1@example');
    await submit();

    http.expectNone(`${API_URL}/users`);
    expect(page().textContent).toContain('like name@example.com');
  });

  it('tells the person the password rules up front', () => {
    expect(page().textContent).toContain('upper-case letter');
    expect(page().textContent).toContain('digit');
  });

  it('creates the user with the chosen role, then closes with the new user', async () => {
    await fill();
    form().controls.role.setValue('Manager');

    await submit();

    const request = http.expectOne(`${API_URL}/users`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'new@example.com', password: 'Str0ng-Pass!', role: 'Manager' });
    request.flush(created);

    expect(close).toHaveBeenCalledWith(created);
  });

  it('cannot be submitted twice while saving', async () => {
    await fill();
    await submit();

    expect(submitButton().disabled).toBe(true);
    expect(submitButton().textContent).toContain('Creating');
    http.expectOne(`${API_URL}/users`).flush(created);
  });

  it('puts "already registered" next to the email, and keeps the dialog open with the typed text', async () => {
    await fill('taken@example.com');
    await submit();

    http.expectOne(`${API_URL}/users`).flush(
      { errors: { Email: ["Email 'taken@example.com' is already taken."] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page().textContent).toContain('is already taken');
    expect((page().querySelector('input[type="email"]') as HTMLInputElement).value).toBe('taken@example.com');
    expect(close).not.toHaveBeenCalled();
    expect(submitButton().disabled).toBe(false);
  });

  it('puts a weak-password complaint next to the password', async () => {
    await fill('new@example.com', 'weak');
    await submit();

    http.expectOne(`${API_URL}/users`).flush(
      { errors: { Password: ['Passwords must have at least one uppercase letter.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page().textContent).toContain('Passwords must have at least one uppercase letter.');
    expect(close).not.toHaveBeenCalled();
  });

  it('says so when the server cannot be reached', async () => {
    await fill();
    await submit();

    http.expectOne(`${API_URL}/users`).error(new ProgressEvent('error'));
    await settle();

    expect(page().textContent).toContain('Cannot reach the server');
    expect(close).not.toHaveBeenCalled();
  });
});

describe('ResetPasswordDialog', () => {
  let fixture: ComponentFixture<ResetPasswordDialog>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const passwordBoxes = () => page().querySelectorAll('input[type="password"]');

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function fill(first: string, second: string) {
    const [one, two] = Array.from(passwordBoxes()) as HTMLInputElement[];
    one.value = first;
    one.dispatchEvent(new Event('input'));
    two.value = second;
    two.dispatchEvent(new Event('input'));
    two.dispatchEvent(new Event('blur'));
    await settle();
  }

  async function submit() {
    page().querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();
  }

  beforeEach(async () => {
    ({ http, close } = await configure(ResetPasswordDialog, { userId: 'u-9', email: 'friend1@example.com' }));
    fixture = TestBed.createComponent(ResetPasswordDialog);
    await settle();
  });

  it('says whose password it is, and what will happen', () => {
    expect(page().textContent).toContain('friend1@example.com');
    expect(page().textContent).toContain('signed out everywhere');
  });

  it('does not save when the two boxes differ, so a typing slip cannot lock someone out', async () => {
    await fill('Str0ng-Pass!', 'Str0ng-Pasx!');
    await submit();

    http.expectNone(`${API_URL}/users/u-9/password`);
    expect(page().textContent).toContain('do not match');
  });

  it('does not save empty boxes', async () => {
    await submit();

    http.expectNone(`${API_URL}/users/u-9/password`);
    expect(page().textContent).toContain('A password is required.');
  });

  it('sends the new password for that person, then closes', async () => {
    await fill('Str0ng-Pass!', 'Str0ng-Pass!');
    await submit();

    const request = http.expectOne(`${API_URL}/users/u-9/password`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ newPassword: 'Str0ng-Pass!' });
    request.flush({ id: 'u-9', email: 'friend1@example.com', roles: ['Employee'], isActive: true });

    expect(close).toHaveBeenCalledWith(true);
  });

  it('shows the API\'s password rule complaint and stays open', async () => {
    await fill('weak', 'weak');
    await submit();

    http.expectOne(`${API_URL}/users/u-9/password`).flush(
      { errors: { NewPassword: ['Passwords must be at least 8 characters.'] } },
      { status: 400, statusText: 'Bad Request' },
    );
    await settle();

    expect(page().textContent).toContain('Passwords must be at least 8 characters.');
    expect(close).not.toHaveBeenCalled();
  });

  it('says so when the server cannot be reached', async () => {
    await fill('Str0ng-Pass!', 'Str0ng-Pass!');
    await submit();

    http.expectOne(`${API_URL}/users/u-9/password`).error(new ProgressEvent('error'));
    await settle();

    expect(page().textContent).toContain('Cannot reach the server');
  });
});

describe('ChangeRoleDialog', () => {
  let fixture: ComponentFixture<ChangeRoleDialog>;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const confirm = () => Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Change role') as HTMLButtonElement;
  const control = () => (fixture.componentInstance as unknown as { role: import('@angular/forms').FormControl<string | null> }).role;

  beforeEach(async () => {
    ({ close } = await configure(ChangeRoleDialog, { email: 'lee@example.com', current: 'Employee' }));
    fixture = TestBed.createComponent(ChangeRoleDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('says whose role it is, and that they are signed out', () => {
    expect(page().textContent).toContain('lee@example.com');
    expect(page().textContent).toContain('signed out at once');
  });

  it('cannot be confirmed until a different role is chosen', () => {
    expect(confirm().disabled).toBe(true);

    control().setValue('Manager');
    fixture.detectChanges();
    expect(confirm().disabled).toBe(false);

    control().setValue('Employee');
    fixture.detectChanges();
    expect(confirm().disabled).toBe(true);
  });

  it('closes with the new role', () => {
    control().setValue('Admin');
    fixture.detectChanges();

    confirm().click();

    expect(close).toHaveBeenCalledWith('Admin');
  });
});
