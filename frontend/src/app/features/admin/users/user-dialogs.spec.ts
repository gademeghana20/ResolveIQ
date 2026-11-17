import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { API_URL } from '../../../core/api-config';
import { UserSummary } from '../../../core/user.models';
import { ChangeRoleDialog } from './change-role-dialog';
import { CreateUserDialog } from './create-user-dialog';

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

  async function fill(email = 'new@example.com') {
    typeInto(page(), 'input[type="email"]', email);
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

  it('has no password box: the new person chooses their own through an emailed link', () => {
    expect(page().querySelector('input[type="password"]')).toBeNull();
    expect(page().textContent).toContain('emailed a link to choose their own');
  });

  it('creates the user with the chosen role, then closes with the new user', async () => {
    await fill();
    form().controls.role.setValue('Manager');

    await submit();

    const request = http.expectOne(`${API_URL}/users`);
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ email: 'new@example.com', role: 'Manager' }); // no password is ever sent
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

  it('says so when the server cannot be reached', async () => {
    await fill();
    await submit();

    http.expectOne(`${API_URL}/users`).error(new ProgressEvent('error'));
    await settle();

    expect(page().textContent).toContain('Cannot reach the server');
    expect(close).not.toHaveBeenCalled();
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
