import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideRouter } from '@angular/router';
import { of } from 'rxjs';

import { API_URL } from '../../../core/api-config';
import { drawGrid } from '../../../shared/data-grid/grid-testing';
import { UserSummary } from '../../../core/user.models';
import { fakeToken } from '../../../testing/fake-token';
import { Users } from './users';

// fakeToken() says the signed-in person's id is "user-1".
const me: UserSummary = { id: 'user-1', email: 'admin@example.com', roles: ['Admin'], isActive: true };
const dana: UserSummary = { id: 'u-dana', email: 'dana@example.com', roles: ['Employee'], isActive: true };
const lee: UserSummary = { id: 'u-lee', email: 'Lee@example.com', roles: ['Manager'], isActive: false };

describe('Users', () => {
  let fixture: ComponentFixture<Users>;
  let http: HttpTestingController;
  let dialog: MatDialog;
  let snack: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const rows = () => Array.from(page().querySelectorAll('.ag-row'));
  const rowFor = (email: string) => rows().find((r) => r.textContent!.includes(email)) as HTMLElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await drawGrid(() => fixture.detectChanges());
  }

  async function open(users: UserSummary[] = [dana, me, lee]) {
    localStorage.clear();
    localStorage.setItem('resolveiq.session', JSON.stringify({ token: fakeToken(), email: me.email, roles: ['Admin'] }));

    await TestBed.configureTestingModule({
      imports: [Users],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    dialog = TestBed.inject(MatDialog);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fixture = TestBed.createComponent(Users);
    await settle();

    http.expectOne(`${API_URL}/users`).flush(users);
    await settle();
  }

  /** Pretends the dialog was answered, instead of driving the dialog's own screen (it has its own tests). */
  function dialogReturns(value: unknown) {
    return vi.spyOn(dialog, 'open').mockReturnValue({ afterClosed: () => of(value) } as never);
  }

  async function chooseFromMenu(email: string, item: string) {
    (rowFor(email).querySelector('button[mat-icon-button]') as HTMLButtonElement).click();
    await settle();

    const entry = Array.from(document.querySelectorAll('button[mat-menu-item]')).find((b) => b.textContent!.trim().startsWith(item)) as HTMLButtonElement;
    entry.click();
    await settle();
  }

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  describe('the list', () => {
    it('shows everyone, alphabetically, with role and status', async () => {
      await open();

      expect(rows().map((r) => r.querySelector('.ag-cell[col-id="email"]')!.textContent!.replace('(you)', '').trim())).toEqual([
        'admin@example.com',
        'dana@example.com',
        'Lee@example.com',
      ]);
      expect(rowFor('dana@example.com').textContent).toContain('Employee');
      expect(rowFor('dana@example.com').textContent).toContain('Active');
      expect(rowFor('Lee@example.com').textContent).toContain('Manager');
      expect(rowFor('Lee@example.com').textContent).toContain('Inactive');
    });

    it('marks your own row, and offers no actions on it, since you cannot change or deactivate yourself', async () => {
      await open();

      expect(rowFor('admin@example.com').textContent).toContain('(you)');
      expect(rowFor('admin@example.com').querySelector('button[mat-icon-button]')).toBeNull();
      expect(rowFor('dana@example.com').querySelector('button[mat-icon-button]')).not.toBeNull();
    });

    it('finds people by part of their email', async () => {
      await open();

      const box = page().querySelector('input[type="search"]') as HTMLInputElement;
      box.value = 'DAN';
      box.dispatchEvent(new Event('input'));
      await settle();

      expect(rows().length).toBe(1);
      expect(rows()[0].textContent).toContain('dana@example.com');
    });

    it('says so when nobody matches', async () => {
      await open();

      const box = page().querySelector('input[type="search"]') as HTMLInputElement;
      box.value = 'zzz';
      box.dispatchEvent(new Event('input'));
      await settle();

      expect(text()).toContain('No one matches "zzz".');
    });

    it('says so, and recovers, when the users cannot be loaded', async () => {
      localStorage.clear();
      localStorage.setItem('resolveiq.session', JSON.stringify({ token: fakeToken(), email: me.email, roles: ['Admin'] }));
      await TestBed.configureTestingModule({
        imports: [Users],
        providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
      }).compileComponents();
      http = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(Users);
      await settle();

      http.expectOne(`${API_URL}/users`).flush({}, { status: 500, statusText: 'Server Error' });
      await settle();
      expect(text()).toContain('The users could not be loaded.');

      Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('Try again'))!.click();
      await settle();
      http.expectOne(`${API_URL}/users`).flush([dana]);
      await settle();

      expect(rows().length).toBe(1);
    });
  });

  describe('creating a user', () => {
    it('adds the new person to the table, and says so', async () => {
      await open();
      const newcomer: UserSummary = { id: 'u-new', email: 'zed@example.com', roles: ['Manager'], isActive: true };
      dialogReturns(newcomer);

      (Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('New user')) as HTMLButtonElement).click();
      await settle();

      expect(rowFor('zed@example.com')).toBeDefined();
      expect(snack.mock.calls[0][0]).toContain('zed@example.com was created as Manager');
      expect(snack.mock.calls[0][0]).toContain('choose their own password');
    });

    it('changes nothing when the dialog is cancelled', async () => {
      await open();
      dialogReturns(undefined);

      (Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('New user')) as HTMLButtonElement).click();
      await settle();

      expect(rows().length).toBe(3);
      expect(snack).not.toHaveBeenCalled();
    });
  });

  describe('changing a role', () => {
    it('saves the new role and shows it in the row', async () => {
      await open();
      dialogReturns('Manager');

      await chooseFromMenu('dana@example.com', 'Change role');

      const request = http.expectOne(`${API_URL}/users/u-dana/role`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ role: 'Manager' });
      request.flush({ ...dana, roles: ['Manager'] });
      await settle();

      expect(rowFor('dana@example.com').textContent).toContain('Manager');
      expect(snack.mock.calls[0][0]).toContain('dana@example.com is now Manager');
    });

    it('does nothing when cancelled', async () => {
      await open();
      dialogReturns(undefined);

      await chooseFromMenu('dana@example.com', 'Change role');

      http.expectNone(`${API_URL}/users/u-dana/role`);
    });
  });

  describe('deactivating and reactivating', () => {
    it('asks first, and then marks the person inactive', async () => {
      await open();
      const open$ = dialogReturns(true);

      await chooseFromMenu('dana@example.com', 'Deactivate');

      expect(JSON.stringify(open$.mock.calls[0][1])).toContain('signed out at once');
      const request = http.expectOne(`${API_URL}/users/u-dana/active`);
      expect(request.request.body).toEqual({ isActive: false });
      request.flush({ ...dana, isActive: false });
      await settle();

      expect(rowFor('dana@example.com').textContent).toContain('Inactive');
    });

    it('does nothing if the question is answered "cancel"', async () => {
      await open();
      dialogReturns(false);

      await chooseFromMenu('dana@example.com', 'Deactivate');

      http.expectNone(`${API_URL}/users/u-dana/active`);
    });

    it('brings someone back without asking, since that is harmless', async () => {
      await open();
      const open$ = vi.spyOn(dialog, 'open');

      await chooseFromMenu('Lee@example.com', 'Reactivate');

      expect(open$).not.toHaveBeenCalled();
      const request = http.expectOne(`${API_URL}/users/u-lee/active`);
      expect(request.request.body).toEqual({ isActive: true });
      request.flush({ ...lee, isActive: true });
      await settle();

      expect(rowFor('Lee@example.com').textContent).toContain('Active');
      expect(rowFor('Lee@example.com').textContent).not.toContain('Inactive');
    });
  });

  describe('sending a password reset email', () => {
    it('asks first, then emails that person, and says so', async () => {
      await open();
      const open$ = dialogReturns(true);

      await chooseFromMenu('dana@example.com', 'Send password reset email');

      expect(open$.mock.calls[0][1]).toMatchObject({ data: { title: expect.stringContaining('dana@example.com') } });
      const request = http.expectOne(`${API_URL}/users/u-dana/password-reset-email`);
      expect(request.request.method).toBe('POST');
      request.flush(dana);
      await settle();

      expect(snack.mock.calls[0][0]).toBe('An email was sent to dana@example.com.');
    });

    it('sends nothing when the question is answered "cancel"', async () => {
      await open();
      dialogReturns(undefined);

      await chooseFromMenu('dana@example.com', 'Send password reset email');

      http.expectNone(`${API_URL}/users/u-dana/password-reset-email`);
      expect(snack).not.toHaveBeenCalled();
    });

    it('explains a refusal, for example a deactivated person who gets no email', async () => {
      await open();
      dialogReturns(true);

      await chooseFromMenu('Lee@example.com', 'Send password reset email');
      http.expectOne(`${API_URL}/users/u-lee/password-reset-email`).flush(
        { errors: { Id: ['This person is deactivated, so no email was sent. Reactivate them first.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();

      expect(snack.mock.calls[0][0]).toContain('deactivated');
      http.expectOne(`${API_URL}/users`).flush([dana, me, lee]); // the list reloads, as after any refusal
    });
  });

  describe('when a change is refused', () => {
    it('shows the API\'s reason and reloads, because the screen may be out of date', async () => {
      await open();
      dialogReturns('Admin');

      await chooseFromMenu('dana@example.com', 'Change role');
      http.expectOne(`${API_URL}/users/u-dana/role`).flush(
        { errors: { Role: ['The role must be Admin, Manager or Employee.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();

      expect(snack.mock.calls[0][0]).toBe('The role must be Admin, Manager or Employee.');
      http.expectOne(`${API_URL}/users`).flush([dana, me, lee]); // a fresh load
    });
  });
});
