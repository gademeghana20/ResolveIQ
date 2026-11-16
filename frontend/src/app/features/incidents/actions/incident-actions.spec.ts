import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';

import { API_URL } from '../../../core/api-config';
import { IncidentActions as IncidentActionsModel, IncidentDetail } from '../incident.models';
import { IncidentActions } from './incident-actions';

const ID = 'inc-1';

function incident(actions: Partial<IncidentActionsModel> = {}, overrides: Partial<IncidentDetail> = {}): IncidentDetail {
  return {
    id: ID,
    incidentNumber: 'INC-000001',
    title: 'Printer broken',
    description: 'Paper jam',
    priority: 'High',
    status: 'InProgress',
    categoryId: null,
    categoryName: null,
    reportedByUserId: 'u1',
    assignedToUserId: 'u2',
    reportedByEmail: 'reporter@example.com',
    assignedToEmail: 'dana@example.com',
    createdAtUtc: '2026-11-16T18:00:00Z',
    updatedAtUtc: null,
    resolvedAtUtc: null,
    slaState: 'OnTrack',
    slaStartedAtUtc: null,
    slaDueAtUtc: null,
    actions: { canAssign: false, canChangePriority: false, allowedStatusChanges: [], ...actions },
    ...overrides,
  };
}

describe('IncidentActions', () => {
  let fixture: ComponentFixture<IncidentActions>;
  let http: HttpTestingController;
  let changed: IncidentDetail[];
  let stale: number;
  let snack: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const labels = () => Array.from(page().querySelectorAll('button')).map((b) => b.textContent!.trim());
  const buttonNamed = (name: string) =>
    Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.trim() === name)!;
  const inDialog = (selector: string) => document.querySelector(`mat-dialog-container ${selector}`) as HTMLElement | null;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function setUp(value: IncidentDetail) {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [IncidentActions],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        // No animations in tests, so dialogs open and close straight away.
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fixture = TestBed.createComponent(IncidentActions);
    fixture.componentRef.setInput('incident', value);
    changed = [];
    stale = 0;
    fixture.componentInstance.changed.subscribe((c) => changed.push(c));
    fixture.componentInstance.stale.subscribe(() => stale++);
    await settle();
  }

  afterEach(() => TestBed.inject(MatDialog).closeAll());

  it('shows nothing at all when the person may do nothing', async () => {
    await setUp(incident());

    expect(labels()).toEqual([]);
  });

  it('shows only the actions the API allows', async () => {
    await setUp(incident({ allowedStatusChanges: ['OnHold', 'Resolved'], canAssign: true, canChangePriority: false }));

    expect(labels()).toEqual(['Put on hold…', 'Mark resolved', 'Reassign…']);
  });

  it('says Assign when nobody is assigned yet, and offers a priority change when allowed', async () => {
    await setUp(incident({ canAssign: true, canChangePriority: true }, { assignedToUserId: null, assignedToEmail: null }));

    expect(labels()).toEqual(['Assign…', 'Change priority…']);
  });

  it('names each status move clearly', async () => {
    await setUp(incident({ allowedStatusChanges: ['InProgress', 'Closed'] }, { status: 'Resolved' }));
    expect(labels()).toEqual(['Reopen', 'Close incident…']);
  });

  it('moves an incident with one click when no more information is needed', async () => {
    await setUp(incident({ allowedStatusChanges: ['Resolved'] }));
    const updated = incident({}, { status: 'Resolved' });

    buttonNamed('Mark resolved').click();
    const request = http.expectOne(`${API_URL}/incidents/${ID}/status`);
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toMatchObject({ status: 'Resolved' });
    request.flush(updated);

    expect(changed).toEqual([updated]);
  });

  it('cannot be clicked again while a change is being saved', async () => {
    await setUp(incident({ allowedStatusChanges: ['Resolved'], canAssign: true }));

    buttonNamed('Mark resolved').click();
    await settle();

    expect(buttonNamed('Mark resolved').disabled).toBe(true);
    expect(buttonNamed('Reassign…').disabled).toBe(true);

    http.expectOne(`${API_URL}/incidents/${ID}/status`).flush(incident());
    await settle();
    expect(buttonNamed('Mark resolved').disabled).toBe(false);
  });

  describe('putting an incident on hold', () => {
    async function open() {
      await setUp(incident({ allowedStatusChanges: ['OnHold'] }));
      buttonNamed('Put on hold…').click();
      await settle();
    }

    it('asks for a reason first, and sends it', async () => {
      await open();
      http.expectNone(`${API_URL}/incidents/${ID}/status`); // nothing sent yet

      const box = inDialog('textarea') as HTMLTextAreaElement;
      box.value = '  Waiting for a part  ';
      box.dispatchEvent(new Event('input'));
      Array.from(document.querySelectorAll('mat-dialog-container button')).find((b) => b.textContent!.trim() === 'Put on hold')!.dispatchEvent(new Event('click'));
      await settle();

      const request = http.expectOne(`${API_URL}/incidents/${ID}/status`);
      expect(request.request.body).toEqual({ status: 'OnHold', reason: 'Waiting for a part' });
      request.flush(incident({}, { status: 'OnHold' }));
    });

    it('refuses to continue without a reason', async () => {
      await open();

      Array.from(document.querySelectorAll('mat-dialog-container button')).find((b) => b.textContent!.trim() === 'Put on hold')!.dispatchEvent(new Event('click'));
      await settle();

      http.expectNone(`${API_URL}/incidents/${ID}/status`);
      expect(document.querySelector('mat-dialog-container')?.textContent).toContain('A reason is required.');
    });

    it('sends nothing when cancelled', async () => {
      await open();

      Array.from(document.querySelectorAll('mat-dialog-container button')).find((b) => b.textContent!.trim() === 'Cancel')!.dispatchEvent(new Event('click'));
      await settle();

      http.expectNone(`${API_URL}/incidents/${ID}/status`);
    });
  });

  describe('closing an incident', () => {
    async function open() {
      await setUp(incident({ allowedStatusChanges: ['Closed'] }, { status: 'Resolved' }));
      buttonNamed('Close incident…').click();
      await settle();
    }

    it('asks "are you sure?" and sends nothing until confirmed', async () => {
      await open();

      expect(document.querySelector('mat-dialog-container')?.textContent).toContain('cannot be reopened');
      http.expectNone(`${API_URL}/incidents/${ID}/status`);
    });

    it('closes it once confirmed', async () => {
      await open();

      Array.from(document.querySelectorAll('mat-dialog-container button')).find((b) => b.textContent!.trim() === 'Close incident')!.dispatchEvent(new Event('click'));
      await settle();

      const request = http.expectOne(`${API_URL}/incidents/${ID}/status`);
      expect(request.request.body).toMatchObject({ status: 'Closed' });
      request.flush(incident({}, { status: 'Closed' }));
    });

    it('does nothing when cancelled', async () => {
      await open();

      Array.from(document.querySelectorAll('mat-dialog-container button')).find((b) => b.textContent!.trim() === 'Cancel')!.dispatchEvent(new Event('click'));
      await settle();

      http.expectNone(`${API_URL}/incidents/${ID}/status`);
    });
  });

  describe('when the API refuses', () => {
    async function failWith(status: number, body: object = {}) {
      await setUp(incident({ allowedStatusChanges: ['Resolved'] }));
      buttonNamed('Mark resolved').click();
      http.expectOne(`${API_URL}/incidents/${ID}/status`).flush(body, { status, statusText: String(status) });
      await settle();
    }

    it('explains a validation problem in the words the API gave', async () => {
      await failWith(400, { errors: { Status: ['That change is not allowed.'] } });

      expect(snack).toHaveBeenCalledWith('That change is not allowed.', 'Dismiss', expect.anything());
      expect(changed).toEqual([]);
      expect(buttonNamed('Mark resolved').disabled).toBe(false); // can try again
    });

    it('asks the page to reload when someone else changed the incident (409)', async () => {
      await failWith(409);

      expect(snack.mock.calls[0][0]).toContain('Someone else changed this incident');
      expect(stale).toBe(1);
    });

    it('asks the page to reload when permissions changed (403)', async () => {
      await failWith(403);

      expect(snack.mock.calls[0][0]).toContain('not allowed');
      expect(stale).toBe(1);
    });

    it('says so when the server cannot be reached, without asking for a reload', async () => {
      await setUp(incident({ allowedStatusChanges: ['Resolved'] }));
      buttonNamed('Mark resolved').click();
      http.expectOne(`${API_URL}/incidents/${ID}/status`).error(new ProgressEvent('error'));
      await settle();

      expect(snack.mock.calls[0][0]).toContain('Cannot reach the server');
      expect(stale).toBe(0);
    });
  });

  it('changes the priority through its dialog', async () => {
    await setUp(incident({ canChangePriority: true }));

    buttonNamed('Change priority…').click();
    await settle();
    expect(document.querySelector('mat-dialog-container')?.textContent).toContain('restarts the SLA clock');

    // Nothing is chosen differently yet, so the confirm button is disabled.
    const confirm = Array.from(document.querySelectorAll('mat-dialog-container button')).find((b) => b.textContent!.trim() === 'Change priority') as HTMLButtonElement;
    expect(confirm.disabled).toBe(true);
  });
});
