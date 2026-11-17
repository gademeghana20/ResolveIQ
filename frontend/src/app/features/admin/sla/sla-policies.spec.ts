import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';

import { API_URL } from '../../../core/api-config';
import { drawGrid } from '../../../shared/data-grid/grid-testing';
import { SlaPolicies } from './sla-policies';
import { SlaPolicy } from './sla.models';

// The API sends them least urgent first; the page must still show the urgent ones on top.
const policies: SlaPolicy[] = [
  { priority: 'Low', resolutionTargetHours: 48, isActive: true },
  { priority: 'Medium', resolutionTargetHours: 30, isActive: true },
  { priority: 'High', resolutionTargetHours: 1, isActive: false },
  { priority: 'Critical', resolutionTargetHours: 4, isActive: true },
];

describe('SlaPolicies', () => {
  let fixture: ComponentFixture<SlaPolicies>;
  let http: HttpTestingController;
  let dialog: MatDialog;
  let snack: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const rows = () => Array.from(page().querySelectorAll('.ag-row')) as HTMLElement[];
  const rowFor = (priority: string) => rows().find((r) => r.querySelector('app-badge')!.textContent!.trim() === priority)!;
  const listRequest = () => http.expectOne(`${API_URL}/sla/policies`);

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    await drawGrid(() => fixture.detectChanges());
  }

  async function create() {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [SlaPolicies],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    dialog = TestBed.inject(MatDialog);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fixture = TestBed.createComponent(SlaPolicies);
    await settle();
  }

  async function open(list: SlaPolicy[] = policies) {
    await create();
    listRequest().flush(list);
    await settle();
  }

  const dialogReturns = (value: unknown) =>
    vi.spyOn(dialog, 'open').mockReturnValue({ afterClosed: () => of(value) } as never);

  it('lists the priorities from most to least urgent', async () => {
    await open();

    expect(rows().map((r) => r.querySelector('app-badge')!.textContent!.trim())).toEqual(['Critical', 'High', 'Medium', 'Low']);
  });

  it('writes each deadline in hours, adding days for the long ones', async () => {
    await open();

    expect(rowFor('Critical').textContent).toContain('4 hours');
    expect(rowFor('High').textContent).toContain('1 hour');
    expect(rowFor('High').textContent).not.toContain('1 hours');
    expect(rowFor('Medium').textContent).toContain('30 hours (1 d 6 h)');
    expect(rowFor('Low').textContent).toContain('48 hours (2 d)');
  });

  it('says in words whether each deadline is on or off', async () => {
    await open();

    expect(rowFor('Critical').textContent).toContain('On');
    expect(rowFor('High').textContent).toContain('Off');
  });

  it('explains what an SLA is, and when its clock runs', async () => {
    await open();

    expect(text()).toContain('deadline for resolving an incident');
    expect(text()).toContain('starts when an incident is first assigned');
    expect(text()).toContain('pauses while the incident is on hold');
  });

  it('says a new deadline only affects clocks that start afterwards, and leaves running and finished incidents alone', async () => {
    await open();

    expect(text()).toContain('only applies to incidents whose clock starts afterwards');
    expect(text()).toContain('already running keep the deadline they were given');
    expect(text()).toContain('finished ones keep their result');
    expect(text()).not.toContain('including ones that are already open');
  });

  it('says so, and recovers, when the policies cannot be loaded', async () => {
    await create();
    listRequest().flush({}, { status: 500, statusText: 'Server Error' });
    await settle();
    expect(text()).toContain('The SLA policies could not be loaded.');

    Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('Try again'))!.click();
    await settle();
    listRequest().flush(policies);
    await settle();

    expect(rows().length).toBe(4);
  });

  describe('editing', () => {
    it('opens for that priority', async () => {
      await open();
      const open$ = dialogReturns(undefined);

      (rowFor('Critical').querySelector('button') as HTMLButtonElement).click();
      await settle();

      expect(open$.mock.calls[0][1]).toMatchObject({ data: { policy: { priority: 'Critical', resolutionTargetHours: 4 } } });
    });

    it('shows the saved hours straight away, and says so', async () => {
      await open();
      dialogReturns({ priority: 'Critical', resolutionTargetHours: 2, isActive: true });

      (rowFor('Critical').querySelector('button') as HTMLButtonElement).click();
      await settle();

      expect(rowFor('Critical').textContent).toContain('2 hours');
      expect(snack.mock.calls[0][0]).toBe('Critical incidents now have 2 hours to be resolved.');
    });

    it('says when a priority no longer has a deadline', async () => {
      await open();
      dialogReturns({ priority: 'Critical', resolutionTargetHours: 4, isActive: false });

      (rowFor('Critical').querySelector('button') as HTMLButtonElement).click();
      await settle();

      expect(rowFor('Critical').textContent).toContain('Off');
      expect(snack.mock.calls[0][0]).toBe('Critical incidents no longer have a deadline.');
    });

    it('changes nothing when cancelled', async () => {
      await open();
      dialogReturns(undefined);

      (rowFor('Critical').querySelector('button') as HTMLButtonElement).click();
      await settle();

      expect(rowFor('Critical').textContent).toContain('4 hours');
      expect(snack).not.toHaveBeenCalled();
    });

    it('keeps the other priorities as they were', async () => {
      await open();
      dialogReturns({ priority: 'Critical', resolutionTargetHours: 2, isActive: true });

      (rowFor('Critical').querySelector('button') as HTMLButtonElement).click();
      await settle();

      expect(rowFor('High').textContent).toContain('1 hour');
      expect(rowFor('Low').textContent).toContain('48 hours');
    });
  });
});
