import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting, TestRequest } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatSelect } from '@angular/material/select';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';

import { API_URL } from '../../../core/api-config';
import { drawGrid, gridRows } from '../../../shared/data-grid/grid-testing';
import { AuditLine, AuditPage } from './audit.models';
import { AuditLog } from './audit-log';

function line(overrides: Partial<AuditLine> = {}): AuditLine {
  return {
    id: 'a1',
    createdAtUtc: '2026-11-18T09:30:00Z',
    actorEmail: 'admin@example.com',
    action: 'user.role_changed',
    targetType: 'User',
    targetLabel: 'dana@example.com',
    details: 'Role: Employee → Manager',
    ...overrides,
  };
}

function page(items: AuditLine[], totalCount = items.length): AuditPage {
  return { items, page: 1, pageSize: 20, totalCount, totalPages: Math.ceil(totalCount / 20) };
}

describe('AuditLog', () => {
  let fixture: ComponentFixture<AuditLog>;
  let http: HttpTestingController;

  const root = () => fixture.nativeElement as HTMLElement;
  const text = () => root().textContent ?? '';
  const isAudit = (r: { url: string }) => r.url === `${API_URL}/audit`;

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await drawGrid(() => fixture.detectChanges());
  }

  async function load(items: AuditLine[], total?: number): Promise<TestRequest> {
    await settle();
    const request = http.expectOne(isAudit);
    request.flush(page(items, total));
    await settle();
    return request;
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [AuditLog],
      providers: [
        provideRouter([]),
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();
    http = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(AuditLog);
  });

  it('asks for the first page, 20 lines', async () => {
    await settle();
    const request = http.expectOne(isAudit);

    expect(request.request.params.get('page')).toBe('1');
    expect(request.request.params.get('pageSize')).toBe('20');
    expect(request.request.params.has('action')).toBe(false);
    request.flush(page([]));
  });

  it('shows who did what to whom, with friendly words for the action', async () => {
    await load([line()]);

    const [row] = gridRows(root());
    expect(row.join(' | ')).toContain('admin@example.com');
    expect(row.join(' | ')).toContain('Role changed');
    expect(row.join(' | ')).toContain('dana@example.com');
    expect(row.join(' | ')).toContain('Role: Employee → Manager');
  });

  it('names the app when nobody did it', async () => {
    await load([line({ actorEmail: null })]);

    expect(text()).toContain('ResolveIQ (automatic)');
  });

  it('says so when nothing has been recorded', async () => {
    await load([]);

    expect(text()).toContain('Nothing has been recorded yet.');
  });

  it('filters by action, starting again from page 1', async () => {
    await load([line()], 57);

    fixture.debugElement.query(By.directive(MatSelect)).componentInstance.selectionChange.emit({ value: 'user.created' });
    await settle();

    const request = http.expectOne(isAudit);
    expect(request.request.params.get('action')).toBe('user.created');
    expect(request.request.params.get('page')).toBe('1');
    request.flush(page([]));
    await settle();
    expect(text()).toContain('No lines match your filters.');
  });

  it('searches after a short pause', async () => {
    await load([line()]);
    const input = root().querySelector('input[type="search"]') as HTMLInputElement;
    input.value = 'dana';
    input.dispatchEvent(new Event('input'));

    http.expectNone(isAudit); // not on every keystroke
    await new Promise((resolve) => setTimeout(resolve, 400));
    fixture.detectChanges();

    const request = http.expectOne(isAudit);
    expect(request.request.params.get('search')).toBe('dana');
    request.flush(page([]));
  });

  it('says so when it cannot load, and can try again', async () => {
    await settle();
    http.expectOne(isAudit).flush({}, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(text()).toContain('The audit log could not be loaded.');
    (Array.from(root().querySelectorAll('button')).find((b) => b.textContent!.includes('Try again')) as HTMLButtonElement).click();
    await settle();
    http.expectOne(isAudit).flush(page([line()]));
  });

  it('shows the page range in a footer', async () => {
    await load([line()], 57);

    expect(text()).toContain('57');
  });
});
