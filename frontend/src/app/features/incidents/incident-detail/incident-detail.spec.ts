import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatDialog } from '@angular/material/dialog';
import { of } from 'rxjs';

import { provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { AuthService } from '../../../core/auth/auth.service';
import { HistoryEntry, IncidentComment, IncidentDetail as IncidentDetailModel } from '../incident.models';
import { IncidentDetail } from './incident-detail';

const ID = 'abc-123';

function detail(overrides: Partial<IncidentDetailModel> = {}): IncidentDetailModel {
  return {
    id: ID,
    incidentNumber: 'INC-000007',
    title: 'VPN keeps dropping',
    description: 'Line one\nLine two',
    priority: 'High',
    status: 'InProgress',
    categoryId: 'c1',
    categoryName: 'Network',
    reportedByUserId: 'u1',
    assignedToUserId: 'u2',
    reportedByEmail: 'reporter@example.com',
    assignedToEmail: 'dana@example.com',
    createdAtUtc: '2026-11-16T18:00:00Z',
    updatedAtUtc: null,
    resolvedAtUtc: null,
    closedAtUtc: null,
    reportedUrgency: null,
    resolutionNotes: null,
    autoCloseAtUtc: null,
    slaState: 'AtRisk',
    slaStartedAtUtc: '2026-11-16T18:30:00Z',
    slaDueAtUtc: '2026-11-17T02:30:00Z',
    actions: { canEdit: false, canAssign: false, canChangePriority: false, allowedStatusChanges: [], onBehalfOf: [] },
    ...overrides,
  };
}

const history: HistoryEntry[] = [
  { fieldChanged: 'AssignedTo', oldValue: null, newValue: 'dana@example.com', changedBy: 'manager@example.com', changedAtUtc: '2026-11-16T18:30:00Z' },
  { fieldChanged: 'Status', oldValue: 'Open', newValue: 'InProgress', changedBy: 'dana@example.com', changedAtUtc: '2026-11-16T19:00:00Z' },
];

const comments: IncidentComment[] = [
  { id: 'k1', text: 'Looking into it', authorUserId: 'u2', authorEmail: 'dana@example.com', createdAtUtc: '2026-11-16T19:05:00Z' },
];

describe('IncidentDetail', () => {
  let fixture: ComponentFixture<IncidentDetail>;
  let http: HttpTestingController;

  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [IncidentDetail],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(IncidentDetail);
    fixture.componentRef.setInput('id', ID);
    await settle();
  });

  // The page abandons its other requests when the main one fails; ignore those cancelled ones.
  function waiting(url: string) {
    const live = http.match(url).filter((request) => !request.cancelled);
    expect(live.length, `requests waiting for ${url}`).toBe(1);
    return live[0];
  }

  const answerDetail = (body: object, status = 200) =>
    waiting(`${API_URL}/incidents/${ID}`).flush(body, { status, statusText: String(status) });
  const answerHistory = (body: object, status = 200) =>
    waiting(`${API_URL}/incidents/${ID}/history`).flush(body, { status, statusText: String(status) });
  const answerComments = (body: object, status = 200) =>
    waiting(`${API_URL}/incidents/${ID}/comments`).flush(body, { status, statusText: String(status) });

  it('asks for the incident, its history and its comments', () => {
    http.expectOne(`${API_URL}/incidents/${ID}`);
    http.expectOne(`${API_URL}/incidents/${ID}/history`);
    http.expectOne(`${API_URL}/incidents/${ID}/comments`);
  });

  it('shows the incident, with names instead of ids', async () => {
    answerDetail(detail());
    answerHistory(history);
    answerComments(comments);
    await settle();

    expect(text()).toContain('INC-000007');
    expect(text()).toContain('VPN keeps dropping');
    expect(text()).toContain('In progress');
    expect(text()).toContain('High');
    expect(text()).toContain('SLA: At risk');
    expect(text()).toContain('reporter@example.com');
    expect(text()).toContain('dana@example.com');
    expect(text()).toContain('Network');
  });

  describe('when it has been resolved', () => {
    const resolved = (overrides: Partial<IncidentDetailModel> = {}) =>
      detail({
        status: 'Resolved',
        resolvedAtUtc: '2026-11-17T09:00:00Z',
        resolutionNotes: 'Replaced the roller.',
        autoCloseAtUtc: '2026-11-24T09:00:00Z',
        reportedByUserId: 'user-1',
        ...overrides,
      });

    async function open(body: IncidentDetailModel, asReporter: boolean) {
      // "user-1" is who the fake sign-in token says the person is.
      (TestBed.inject(AuthService) as unknown as { userId: () => string | null }).userId = () => (asReporter ? 'user-1' : 'someone-else');
      answerDetail(body);
      answerHistory([]);
      answerComments([]);
      await settle();
    }

    it('shows how it was fixed, in its own box', async () => {
      await open(resolved(), true);

      const box = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-card')).find((c) => c.textContent!.includes('How it was fixed'))!;
      expect(box.textContent).toContain('Replaced the roller.');
    });

    it('asks the reporter whether it is fixed, and says when it will close by itself', async () => {
      await open(resolved(), true);

      const notice = (fixture.nativeElement as HTMLElement).querySelector('.notice')!;
      expect(notice.textContent).toContain('Is it fixed?');
      expect(notice.textContent).toContain('reopen it if the problem is still there');
      expect(notice.textContent).toContain('closes automatically on Nov 24, 2026');
    });

    it('tells everyone else it is waiting for the reporter', async () => {
      await open(resolved(), false);

      const notice = (fixture.nativeElement as HTMLElement).querySelector('.notice')!;
      expect(notice.textContent).toContain('Waiting for the person who reported it');
      expect(notice.textContent).not.toContain('Is it fixed?');
    });

    it('does not promise an automatic close when that is switched off', async () => {
      await open(resolved({ autoCloseAtUtc: null }), true);

      expect((fixture.nativeElement as HTMLElement).querySelector('.notice')!.textContent).not.toContain('automatically');
    });

    it('keeps showing how it was fixed after it is closed, with when it was closed', async () => {
      await open(resolved({ status: 'Closed', closedAtUtc: '2026-11-18T10:00:00Z', autoCloseAtUtc: null }), true);

      expect(text()).toContain('How it was fixed');
      expect(text()).toContain('Closed');
      expect(text()).toContain('Nov 18, 2026');
      expect((fixture.nativeElement as HTMLElement).querySelector('.notice')).toBeNull();
    });
  });

  it('shows no resolution box and no notice while the incident is still being worked', async () => {
    answerDetail(detail());
    answerHistory([]);
    answerComments([]);
    await settle();

    expect(text()).not.toContain('How it was fixed');
    expect((fixture.nativeElement as HTMLElement).querySelector('.notice')).toBeNull();
  });

  it('shows the urgency the reporter gave, or says none was given', async () => {
    answerDetail(detail({ reportedUrgency: 'High' }));
    answerHistory([]);
    answerComments([]);
    await settle();
    expect(text()).toContain('Reported urgency');
    expect(text()).toContain('High');
  });

  it('says "Not given" when the reporter gave no urgency', async () => {
    answerDetail(detail({ reportedUrgency: null }));
    answerHistory([]);
    answerComments([]);
    await settle();

    expect(text()).toContain('Not given');
  });

  it('names the app, not a person, for a change it made itself', async () => {
    answerDetail(detail());
    answerHistory([
      { fieldChanged: 'Status', oldValue: 'Resolved', newValue: 'Closed', changedBy: null, changedAtUtc: '2026-11-24T09:00:00Z' },
      { fieldChanged: 'AutoClosed', oldValue: null, newValue: 'Closed automatically after 7 days resolved', changedBy: null, changedAtUtc: '2026-11-24T09:00:00Z' },
    ]);
    answerComments([]);
    await settle();

    expect(text()).toContain('ResolveIQ (automatic)');
    expect(text()).toContain('Closed automatically after 7 days resolved');
  });

  describe('SLA time', () => {
    async function open(minutes: number | null) {
      answerDetail(detail({ slaMinutesLeft: minutes }));
      answerHistory([]);
      answerComments([]);
      await settle();
    }

    it('shows how much time is left while the clock runs', async () => {
      await open(200);

      expect(text()).toContain('Time left');
      expect(text()).toContain('3 h 20 min');
      expect(text()).not.toContain('Overdue by');
    });

    it('shows how late it is, in red, when overdue', async () => {
      await open(-90);

      expect(text()).toContain('Overdue by');
      expect((fixture.nativeElement as HTMLElement).querySelector('dd.overdue')!.textContent).toContain('1 h 30 min');
    });

    it('shows neither when no clock is running', async () => {
      await open(null);

      expect(text()).not.toContain('Time left');
      expect(text()).not.toContain('Overdue by');
    });
  });

  it('keeps the line breaks in the description', async () => {
    answerDetail(detail());
    answerHistory([]);
    answerComments([]);
    await settle();

    const description = (fixture.nativeElement as HTMLElement).querySelector('.text')!;
    expect(description.textContent).toBe('Line one\nLine two');
  });

  it('shows the history as readable sentences', async () => {
    answerDetail(detail());
    answerHistory(history);
    answerComments([]);
    await settle();

    expect(text()).toContain('Assigned to dana@example.com');
    expect(text()).toContain('Status changed from Open to In progress');
    expect(text()).toContain('manager@example.com');
  });

  describe('history of a reworded description', () => {
    const longOld = 'Old wording. '.repeat(100); // well over the old 500-character cut-off
    const longNew = 'New wording. '.repeat(100);

    const descriptionEdit: HistoryEntry = {
      fieldChanged: 'Description',
      oldValue: longOld,
      newValue: longNew,
      changedBy: 'reporter@example.com',
      changedAtUtc: '2026-11-16T20:00:00Z',
    };

    async function openWith(entries: HistoryEntry[]) {
      answerDetail(detail());
      answerHistory(entries);
      answerComments([]);
      await settle();
    }

    it('offers "Show what changed" on a description edit', async () => {
      await openWith([descriptionEdit]);

      expect(text()).toContain('Description edited');
      expect(text()).toContain('Show what changed');
    });

    it('shows the full wording before and after, with nothing cut off', async () => {
      await openWith([descriptionEdit]);

      const versions = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.changes .version'));
      expect(versions.length).toBe(2);
      expect(versions[0].textContent).toContain('Before');
      expect(versions[0].querySelector('.text')!.textContent).toBe(longOld);
      expect(versions[1].textContent).toContain('After');
      expect(versions[1].querySelector('.text')!.textContent).toBe(longNew);
    });

    it('offers it for descriptions only: a title edit already says it all in the sentence', async () => {
      await openWith([
        { fieldChanged: 'Title', oldValue: 'Old title', newValue: 'New title', changedBy: 'reporter@example.com', changedAtUtc: '2026-11-16T20:00:00Z' },
        ...history,
      ]);

      expect(text()).toContain('Title changed from "Old title" to "New title"');
      expect((fixture.nativeElement as HTMLElement).querySelectorAll('.changes').length).toBe(0);
    });
  });

  it('shows the comments with their authors', async () => {
    answerDetail(detail());
    answerHistory([]);
    answerComments(comments);
    await settle();

    expect(text()).toContain('Looking into it');
    expect(text()).toContain('dana@example.com');
  });

  it('shows a comment straight away after it is posted, below the older ones', async () => {
    answerDetail(detail());
    answerHistory([]);
    answerComments(comments);
    await settle();

    const box = (fixture.nativeElement as HTMLElement).querySelector('textarea')!;
    box.value = 'Fixed it';
    box.dispatchEvent(new Event('input'));
    (fixture.nativeElement as HTMLElement).querySelector('form')!.dispatchEvent(new Event('submit'));
    await settle();

    http.expectOne(`${API_URL}/incidents/${ID}/comments`).flush({
      id: 'k2',
      text: 'Fixed it',
      authorUserId: 'u1',
      authorEmail: 'manager@example.com',
      createdAtUtc: '2026-11-16T20:00:00Z',
    });
    await settle();

    const shown = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.comment .text')).map((e) => e.textContent);
    expect(shown).toEqual(['Looking into it', 'Fixed it']);
  });

  it('offers no comment form on a closed incident, and explains why', async () => {
    answerDetail(detail({ status: 'Closed' }));
    answerHistory([]);
    answerComments(comments);
    await settle();

    expect((fixture.nativeElement as HTMLElement).querySelector('textarea')).toBeNull();
    expect(text()).toContain('Comments are closed on a closed incident.');
  });

  it('after an action works, shows the updated incident and fetches the new history line', async () => {
    answerDetail(detail({ status: 'Open', actions: { canEdit: false, canAssign: false, canChangePriority: false, allowedStatusChanges: ['InProgress'], onBehalfOf: [] } }));
    answerHistory([]);
    answerComments([]);
    await settle();

    const start = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Start work')!;
    start.click();
    await settle();

    http.expectOne(`${API_URL}/incidents/${ID}/status`).flush(
      detail({ status: 'InProgress', actions: { canEdit: false, canAssign: false, canChangePriority: false, allowedStatusChanges: ['OnHold', 'Resolved'], onBehalfOf: [] } }),
    );
    await settle();

    // The history is fetched again, because the change added a line to it.
    answerHistory(history);
    await settle();

    expect(text()).toContain('In progress');
    expect(text()).toContain('Mark resolved'); // the buttons now match the new state
    expect(text()).not.toContain('Start work');
    expect(text()).toContain('Status changed from Open to In progress');
  });

  it('reloads the whole page when an action finds it out of date', async () => {
    answerDetail(detail({ actions: { canEdit: false, canAssign: false, canChangePriority: false, allowedStatusChanges: ['Resolved'], onBehalfOf: [] } }));
    answerHistory([]);
    answerComments([]);
    await settle();

    vi.spyOn(TestBed.inject(MatDialog), 'open').mockReturnValue({ afterClosed: () => of('Fixed it.') } as never);
    const resolve = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Mark resolved…')!;
    resolve.click();
    http.expectOne(`${API_URL}/incidents/${ID}/status`).flush({}, { status: 409, statusText: 'Conflict' });
    await settle();

    // A fresh set of requests proves the page reloaded.
    answerDetail(detail({ status: 'Resolved' }));
    answerHistory([]);
    answerComments([]);
  });

  it('says so when there are no comments or history yet', async () => {
    answerDetail(detail({ assignedToUserId: null, assignedToEmail: null, priority: null }));
    answerHistory([]);
    answerComments([]);
    await settle();

    expect(text()).toContain('No comments yet.');
    expect(text()).toContain('No changes yet.');
    expect(text()).toContain('Unassigned');
    expect(text()).toContain('Not set');
  });

  it('still shows the incident when only the history fails to load', async () => {
    answerDetail(detail());
    answerHistory({}, 500);
    answerComments(comments);
    await settle();

    expect(text()).toContain('VPN keeps dropping');
    expect(text()).toContain('History could not be loaded.');
    expect(text()).toContain('Looking into it');
  });

  it('shows a clear message for an incident that is missing or hidden', async () => {
    answerDetail({}, 404);
    await settle();

    expect(text()).toContain('not found, or you do not have access');
    expect(text()).not.toContain('could not be loaded');
  });

  it('offers a retry when the incident fails to load, and recovers', async () => {
    answerDetail({}, 500);
    await settle();
    expect(text()).toContain('The incident could not be loaded.');

    const retry = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) =>
      b.textContent?.includes('Try again'),
    )!;
    retry.click();
    await settle();

    answerDetail(detail({ title: 'Recovered' }));
    answerHistory([]);
    answerComments([]);
    await settle();

    expect(text()).toContain('Recovered');
  });

  it('loads the new incident when the address changes to a different id', async () => {
    answerDetail(detail());
    answerHistory([]);
    answerComments([]);
    await settle();

    fixture.componentRef.setInput('id', 'other-id');
    await settle();

    http.expectOne(`${API_URL}/incidents/other-id`);
    http.expectOne(`${API_URL}/incidents/other-id/history`);
    http.expectOne(`${API_URL}/incidents/other-id/comments`);
  });
});
