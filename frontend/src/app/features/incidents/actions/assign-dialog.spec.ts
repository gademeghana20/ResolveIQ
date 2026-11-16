import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';

import { API_URL } from '../../../core/api-config';
import { UserSummary } from '../incident.models';
import { AssignDialog, AssignDialogData } from './assign-dialog';
import { PriorityDialog } from './priority-dialog';

const users: UserSummary[] = [
  { id: 'u1', email: 'dana@example.com', roles: ['Employee'], isActive: true },
  { id: 'u2', email: 'lee@example.com', roles: ['Employee'], isActive: true },
  { id: 'u3', email: 'gone@example.com', roles: ['Employee'], isActive: false },
];

describe('AssignDialog', () => {
  let fixture: ComponentFixture<AssignDialog>;
  let http: HttpTestingController;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const form = () => (fixture.componentInstance as unknown as { form: AssignDialog['form'] }).form;
  const button = (name: string) =>
    Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.trim() === name) as HTMLButtonElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(data: AssignDialogData) {
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [AssignDialog],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        { provide: MAT_DIALOG_DATA, useValue: data },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AssignDialog);
    await settle();
  }

  it('loads the people, and leaves out anyone who is deactivated', async () => {
    await setUp({ currentAssigneeId: null, currentPriority: null });

    http.expectOne(`${API_URL}/users`).flush(users);
    await settle();

    const people = (fixture.componentInstance as unknown as { people: () => UserSummary[] }).people();
    expect(people.map((p) => p.email)).toEqual(['dana@example.com', 'lee@example.com']);
  });

  it('explains the SLA clock when the incident has no priority yet, and requires both choices', async () => {
    await setUp({ currentAssigneeId: null, currentPriority: null });
    http.expectOne(`${API_URL}/users`).flush(users);
    await settle();

    expect(page().textContent).toContain('no priority yet');

    button('Assign').click();
    await settle();

    expect(close).not.toHaveBeenCalled();
    expect(page().textContent).toContain('Choose who should work on this.');
    expect(page().textContent).toContain('Choose a priority.');
  });

  it('returns the chosen person and priority', async () => {
    await setUp({ currentAssigneeId: null, currentPriority: null });
    http.expectOne(`${API_URL}/users`).flush(users);
    await settle();

    form().setValue({ assignee: 'u2', priority: 'Critical' });
    button('Assign').click();

    expect(close).toHaveBeenCalledWith({ assigneeUserId: 'u2', priority: 'Critical' });
  });

  it('starts from the current assignee and priority when reassigning', async () => {
    await setUp({ currentAssigneeId: 'u1', currentPriority: 'High' });
    http.expectOne(`${API_URL}/users`).flush(users);
    await settle();

    expect(page().textContent).toContain('Reassign incident');
    expect(page().textContent).not.toContain('no priority yet');
    expect(form().getRawValue()).toEqual({ assignee: 'u1', priority: 'High' });

    form().patchValue({ assignee: 'u2' });
    button('Assign').click();

    expect(close).toHaveBeenCalledWith({ assigneeUserId: 'u2', priority: 'High' });
  });

  it('says so, and cannot be confirmed, when the people cannot be loaded', async () => {
    await setUp({ currentAssigneeId: null, currentPriority: null });

    http.expectOne(`${API_URL}/users`).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(page().textContent).toContain('could not be loaded');
    expect(button('Assign').disabled).toBe(true);
  });
});

describe('PriorityDialog', () => {
  let fixture: ComponentFixture<PriorityDialog>;
  let close: ReturnType<typeof vi.fn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const confirm = () =>
    Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Change priority') as HTMLButtonElement;
  const control = () =>
    (fixture.componentInstance as unknown as { priority: import('@angular/forms').FormControl<string | null> }).priority;

  async function setUp(current: 'High' | null) {
    close = vi.fn();
    await TestBed.configureTestingModule({
      imports: [PriorityDialog],
      providers: [
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
        { provide: MAT_DIALOG_DATA, useValue: { current } },
        { provide: MatDialogRef, useValue: { close } },
      ],
    }).compileComponents();

    fixture = TestBed.createComponent(PriorityDialog);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  it('cannot be confirmed until a different priority is chosen', async () => {
    await setUp('High');
    expect(confirm().disabled).toBe(true);

    control().setValue('Critical');
    fixture.detectChanges();
    expect(confirm().disabled).toBe(false);

    control().setValue('High'); // back to the original
    fixture.detectChanges();
    expect(confirm().disabled).toBe(true);
  });

  it('returns the new priority', async () => {
    await setUp('High');

    control().setValue('Low');
    fixture.detectChanges();
    confirm().click();

    expect(close).toHaveBeenCalledWith('Low');
  });

  it('works for an incident that has no priority yet', async () => {
    await setUp(null);
    expect(confirm().disabled).toBe(true);

    control().setValue('Medium');
    fixture.detectChanges();
    confirm().click();

    expect(close).toHaveBeenCalledWith('Medium');
  });
});
