import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { IncidentList } from './incident-list';

/** A link such as /incidents?slaState=Breached (from a dashboard tile) must open the list already filtered. */
describe('IncidentList opened from a link', () => {
  let fixture: ComponentFixture<IncidentList>;
  let http: HttpTestingController;

  const page = () => fixture.nativeElement as HTMLElement;

  async function open(queryParams: Record<string, string>) {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [IncidentList],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(queryParams) } } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(IncidentList);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();

    const request = http.expectOne((r) => r.url === `${API_URL}/incidents`);
    request.flush({ items: [], page: 1, pageSize: 20, totalCount: 0, totalPages: 0 });
    http.expectOne(`${API_URL}/categories`).flush([]);
    await fixture.whenStable();
    fixture.detectChanges();
    return request.request.params;
  }

  it('starts with no filters when the address names none', async () => {
    const params = await open({});

    for (const name of ['slaState', 'status', 'priority', 'assignedToMe', 'reportedByMe', 'unassigned']) {
      expect(params.has(name), name).toBe(false);
    }
    expect(page().textContent).not.toContain('Clear filters');
  });

  it('filters by SLA state', async () => {
    const params = await open({ slaState: 'Breached' });

    expect(params.get('slaState')).toBe('Breached');
    expect(page().textContent).toContain('Clear filters'); // the person can see a filter is on, and remove it
  });

  it('shows only unassigned incidents', async () => {
    const params = await open({ scope: 'unassigned' });

    expect(params.get('unassigned')).toBe('true');
    expect(params.has('assignedToMe')).toBe(false);
    const selected = page().querySelector('mat-button-toggle.mat-button-toggle-checked');
    expect(selected!.textContent).toContain('Unassigned');
  });

  it('shows what is assigned to me, or reported by me', async () => {
    expect((await open({ scope: 'assigned' })).get('assignedToMe')).toBe('true');
  });

  it('can combine a scope with a status and a priority', async () => {
    const params = await open({ status: 'Open', priority: 'High', scope: 'reported' });

    expect(params.get('status')).toBe('Open');
    expect(params.get('priority')).toBe('High');
    expect(params.get('reportedByMe')).toBe('true');
  });

  it('ignores values it does not know, so a hand-edited link cannot break the page', async () => {
    const params = await open({ slaState: 'Nonsense', status: 'Exploded', priority: 'Urgent!!', scope: 'everything' });

    for (const name of ['slaState', 'status', 'priority', 'assignedToMe', 'reportedByMe', 'unassigned']) {
      expect(params.has(name), name).toBe(false);
    }
  });
});
