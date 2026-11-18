import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { DebugElement } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { Router, provideRouter } from '@angular/router';
import { MatSelect } from '@angular/material/select';

import { API_URL } from '../../../core/api-config';
import { clickHeading, drawGrid, gridRows, sortOf } from '../../../shared/data-grid/grid-testing';
import { IncidentSummary, PagedResult } from '../incident.models';
import { IncidentList } from './incident-list';

function incident(overrides: Partial<IncidentSummary> = {}): IncidentSummary {
  return {
    id: 'id-1',
    incidentNumber: 'INC-000001',
    title: 'Printer broken',
    priority: 'High',
    status: 'Open',
    categoryName: 'Hardware',
    reportedByUserId: 'u1',
    assignedToUserId: null,
    reportedByEmail: 'reporter@example.com',
    assignedToEmail: null,
    createdAtUtc: '2026-11-16T18:00:00Z',
    slaState: 'NotStarted',
    slaDueAtUtc: null,
    ...overrides,
  };
}

function page(items: IncidentSummary[], totalCount = items.length): PagedResult<IncidentSummary> {
  return { items, page: 1, pageSize: 20, totalCount, totalPages: Math.ceil(totalCount / 20) };
}

describe('IncidentList', () => {
  let fixture: ComponentFixture<IncidentList>;
  let http: HttpTestingController;

  const isIncidentsCall = (r: { url: string }) => r.url === `${API_URL}/incidents`;
  const text = () => (fixture.nativeElement as HTMLElement).textContent ?? '';

  const root = () => fixture.nativeElement as HTMLElement;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await drawGrid(() => fixture.detectChanges());
  }

  /** Like settle, but without waiting for the table to draw (a wait that long would outlast the search pause). */
  async function settleQuickly() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  /** Answers the list request that is waiting, and the categories request on first load. */
  async function load(items: IncidentSummary[], totalCount?: number): Promise<TestRequest> {
    await settle(); // lets the component notice a changed query and send its request
    const request = http.expectOne(isIncidentsCall);
    request.flush(page(items, totalCount));
    await settle();
    return request;
  }

  beforeEach(async () => {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [IncidentList],
      providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(IncidentList);
    await settle();

    http.expectOne(`${API_URL}/categories`).flush([
      { id: 'c1', name: 'Hardware', description: null, isActive: true },
      { id: 'c2', name: 'Retired', description: null, isActive: false },
    ]);
  });

  it('asks for the newest incidents first, 20 to a page', async () => {
    const request = http.expectOne(isIncidentsCall);

    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.get('sortBy')).toBe('CreatedAt');
    expect(request.request.params.get('sortDirection')).toBe('Desc');
    request.flush(page([]));
  });

  it('marks the Created column as the active sort, newest first', async () => {
    await load([incident()]);

    expect(sortOf(root(), 'Created')).toBe('descending');
    expect(sortOf(root(), 'Title')).toBe('none');
  });

  it('shows each incident as a row, with friendly labels', async () => {
    await load([
      incident(),
      incident({ id: 'id-2', incidentNumber: 'INC-000002', title: 'VPN down', priority: null, status: 'InProgress', assignedToUserId: 'u2', assignedToEmail: 'dana@example.com' }),
    ]);

    expect(gridRows(root()).length).toBe(2);
    expect(text()).toContain('INC-000001');
    expect(text()).toContain('Printer broken');
    expect(text()).toContain('Unassigned');
    expect(text()).toContain('dana@example.com'); // the assignee is named, not just "Assigned"
    expect(text()).toContain('In progress');
    expect(text()).toContain('Not set'); // no priority yet
  });

  it('keeps the SLA state and its due date together in one cell', async () => {
    await load([incident({ slaState: 'OnTrack', slaDueAtUtc: '2026-11-17T18:11:00Z' })]);

    const sla = root().querySelector('.ag-cell[col-id="sla"]')!;
    expect(sla.querySelector('.badge-with-note')).not.toBeNull(); // the badge and its date sit together in one cell
    expect(sla.textContent).toContain('On track');
    expect(sla.textContent).toContain('due Nov 17');
  });

  it('says how much time is left while the clock runs, with the due date as a tooltip', async () => {
    await load([incident({ slaState: 'OnTrack', slaDueAtUtc: '2026-11-17T18:11:00Z', slaMinutesLeft: 200 })]);

    const note = root().querySelector('.ag-cell[col-id="sla"] .badge-note')!;
    expect(note.textContent!.trim()).toBe('3 h 20 min left');
    expect(note.getAttribute('title')).toContain('Due Nov 17');
  });

  it('says how late it is when overdue', async () => {
    await load([incident({ slaState: 'Breached', slaDueAtUtc: '2026-11-17T18:11:00Z', slaMinutesLeft: -45 })]);

    expect(root().querySelector('.ag-cell[col-id="sla"] .badge-note')!.textContent!.trim()).toBe('overdue by 45 min');
  });

  describe('Export CSV', () => {
    const exportButton = () =>
      Array.from(root().querySelectorAll('button')).find((b) => b.textContent!.includes('Export CSV') || b.textContent!.includes('Preparing')) as HTMLButtonElement;
    const exportCall = (r: { url: string }) => r.url === `${API_URL}/incidents/export`;
    let saved: ReturnType<typeof vi.fn<(name: string) => void>>;

    beforeEach(async () => {
      saved = vi.fn<(name: string) => void>();
      vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(function (this: HTMLAnchorElement) {
        saved(this.download);
      });
      (URL as unknown as { createObjectURL: () => string }).createObjectURL = () => 'blob:fake';
      (URL as unknown as { revokeObjectURL: () => void }).revokeObjectURL = () => undefined;
      await load([incident()]);
    });

    it('asks for the same filters, without paging, and saves the file', async () => {
      exportButton().click();
      await settleQuickly();

      const request = http.expectOne(exportCall);
      expect(request.request.params.get('sortBy')).toBe('CreatedAt');
      expect(request.request.params.get('pageSize')).toBe('1'); // paging is ignored by the API for an export
      expect(request.request.responseType).toBe('blob');
      request.flush(new Blob(['x']), { headers: { 'X-Export-Truncated': 'false' } });
      await settleQuickly();

      expect(saved).toHaveBeenCalledWith(expect.stringMatching(/^incidents-\d{4}-\d{2}-\d{2}\.csv$/));
      expect(text()).not.toContain('first 5,000');
    });

    it('says so when the file was cut at the limit', async () => {
      exportButton().click();
      await settleQuickly();

      http.expectOne(exportCall).flush(new Blob(['x']), { headers: { 'X-Export-Truncated': 'true' } });
      await settleQuickly();

      expect(text()).toContain('first 5,000 incidents');
    });

    it('says so when the export fails, and can be tried again', async () => {
      exportButton().click();
      await settleQuickly();

      http.expectOne(exportCall).flush(new Blob(['boom']), { status: 500, statusText: 'Server Error' });
      await settleQuickly();

      expect(text()).toContain('The export did not work');
      expect(saved).not.toHaveBeenCalled();
      expect(exportButton().disabled).toBe(false);
    });

    it('is switched off while the file is being prepared', async () => {
      exportButton().click();
      await settleQuickly();

      expect(exportButton().disabled).toBe(true);
      http.expectOne(exportCall).flush(new Blob(['x']));
    });
  });

  it('has no New incident button of its own (the side menu has one)', async () => {
    await load([incident()]);

    expect(Array.from(root().querySelectorAll('a, button')).some((el) => el.textContent?.includes('New incident'))).toBe(false);
  });

  it('shows the page range and total in a footer of its own', async () => {
    await load([incident()], 57);

    const footer = root().querySelector('app-grid-footer')!;
    expect(footer.textContent).toContain('1–20 of 57');
    expect(footer.textContent).toContain('Page 1 of 3');
    expect(root().querySelector('app-data-grid')!.contains(footer)).toBe(false); // separate from the table
  });

  it('offers an Unassigned view, and asks the API for it', async () => {
    await load([incident()]);

    const toggle = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('mat-button-toggle')).find((t) =>
      t.textContent!.includes('Unassigned'),
    )!;
    (toggle.querySelector('button') as HTMLButtonElement).click();
    await settle();

    const request = http.expectOne(isIncidentsCall);
    expect(request.request.params.get('unassigned')).toBe('true');
    expect(request.request.params.has('assignedToMe')).toBe(false);
    request.flush(page([incident()]));
  });

  it('opens an incident when its row is clicked', async () => {
    await load([incident({ id: 'abc' })]);
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigate').mockResolvedValue(true);

    (fixture.nativeElement as HTMLElement).querySelector<HTMLElement>('.ag-cell[col-id="category"]')!.click();
    await settle();

    expect(navigate).toHaveBeenCalledWith(['/incidents', 'abc']);
  });

  it('says so when there are no incidents at all', async () => {
    await load([]);

    expect(text()).toContain('There are no incidents yet.');
  });

  it('says so when filters leave nothing, and offers to clear them', async () => {
    await load([incident()]);

    const status = fixture.debugElement.queryAll(By.directive(MatSelect))[0];
    status.componentInstance.selectionChange.emit({ value: 'Closed' });
    await load([]);

    expect(text()).toContain('No incidents match your filters.');
    expect(root().querySelector('app-data-grid .grid-empty')?.textContent).toContain('No incidents match your filters.'); // inside the table, not below it
    expect(text()).toContain('Clear filters');
  });

  it('applies a filter, and goes back to page 1', async () => {
    await load([incident()], 60);

    // Move to page 3 first.
    root().querySelector<HTMLButtonElement>('button[aria-label="Last page"]')!.click();
    await settle();
    const onPage3 = http.expectOne(isIncidentsCall);
    expect(onPage3.request.params.get('page')).toBe('3');
    onPage3.flush(page([incident()], 60));
    await settle();

    // Changing a filter must not leave us on page 3.
    const statusSelect: DebugElement = fixture.debugElement.queryAll(By.directive(MatSelect))[0];
    statusSelect.componentInstance.selectionChange.emit({ value: 'Open' });
    await settle();

    const filtered = http.expectOne(isIncidentsCall);
    expect(filtered.request.params.get('status')).toBe('Open');
    expect(filtered.request.params.get('page')).toBe('1');
    filtered.flush(page([incident()]));
  });

  it('only offers active categories in the filter', async () => {
    await load([]);

    const select = fixture.debugElement.queryAll(By.directive(MatSelect))[2].componentInstance as MatSelect;
    select.open();
    await settle();

    const options = Array.from(document.querySelectorAll('mat-option')).map((o) => o.textContent?.trim());
    expect(options).toContain('Hardware');
    expect(options).not.toContain('Retired');
  });

  it('asks the API to sort when a column header is clicked', async () => {
    await load([incident()]);

    clickHeading(root(), 'Priority');
    await settle();

    const request = http.expectOne(isIncidentsCall);
    expect(request.request.params.get('sortBy')).toBe('Priority');
    expect(request.request.params.get('sortDirection')).toBe('Asc');
    request.flush(page([incident()]));
  });

  it('waits for a pause in typing before searching', async () => {
    await load([incident()]);

    const input = (fixture.nativeElement as HTMLElement).querySelector('input[type="search"]') as HTMLInputElement;
    input.value = 'prin';
    input.dispatchEvent(new Event('input'));
    input.value = 'printer';
    input.dispatchEvent(new Event('input'));
    await settleQuickly();
    http.expectNone(isIncidentsCall); // nothing sent while still typing

    await new Promise((resolve) => setTimeout(resolve, 400));
    await settle();

    const request = http.expectOne(isIncidentsCall);
    expect(request.request.params.get('search')).toBe('printer'); // one call, with the final text
    request.flush(page([incident()]));
  });

  it('shows an error with a retry when the API fails, and recovers', async () => {
    await load([incident()]);
    clickHeading(root(), 'Title');
    await settle();

    http.expectOne(isIncidentsCall).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text()).toContain('The incidents could not be loaded.');
    expect(text()).toContain('Printer broken'); // the last good results stay visible

    const retry = Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).find((b) => b.textContent?.includes('Try again'))!;
    retry.click();
    await load([incident({ title: 'Recovered' })]);

    expect(text()).not.toContain('could not be loaded');
    expect(text()).toContain('Recovered');
  });
});
