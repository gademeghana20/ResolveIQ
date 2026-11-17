import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
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
    slaState: 'AtRisk',
    slaStartedAtUtc: '2026-11-16T18:30:00Z',
    slaDueAtUtc: '2026-11-17T02:30:00Z',
    actions: { canAssign: false, canChangePriority: false, allowedStatusChanges: [], onBehalfOf: [] },
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
    answerDetail(detail({ status: 'Open', actions: { canAssign: false, canChangePriority: false, allowedStatusChanges: ['InProgress'], onBehalfOf: [] } }));
    answerHistory([]);
    answerComments([]);
    await settle();

    const start = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Start work')!;
    start.click();
    await settle();

    http.expectOne(`${API_URL}/incidents/${ID}/status`).flush(
      detail({ status: 'InProgress', actions: { canAssign: false, canChangePriority: false, allowedStatusChanges: ['OnHold', 'Resolved'], onBehalfOf: [] } }),
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
    answerDetail(detail({ actions: { canAssign: false, canChangePriority: false, allowedStatusChanges: ['Resolved'], onBehalfOf: [] } }));
    answerHistory([]);
    answerComments([]);
    await settle();

    const resolve = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent!.trim() === 'Mark resolved')!;
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
