import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MATERIAL_ANIMATIONS } from '@angular/material/core';
import { MatDialog } from '@angular/material/dialog';
import { MatSnackBar } from '@angular/material/snack-bar';
import { of } from 'rxjs';

import { API_URL } from '../../../core/api-config';
import { Category } from '../../../core/category.models';
import { Categories } from './categories';

const network: Category = { id: 'c-n', name: 'Network', description: 'Wi-Fi and VPN', isActive: true };
const hardware: Category = { id: 'c-h', name: 'Hardware', description: null, isActive: true };
const legacy: Category = { id: 'c-l', name: 'Archive', description: 'Retired', isActive: false };

describe('Categories', () => {
  let fixture: ComponentFixture<Categories>;
  let http: HttpTestingController;
  let dialog: MatDialog;
  let snack: ReturnType<typeof vi.spyOn>;

  const page = () => fixture.nativeElement as HTMLElement;
  const text = () => page().textContent ?? '';
  const rows = () => Array.from(page().querySelectorAll('tr[mat-row]')) as HTMLElement[];
  const rowFor = (name: string) => rows().find((r) => r.querySelector('.name')!.textContent!.trim() === name)!;
  const buttonIn = (row: HTMLElement, label: string) =>
    Array.from(row.querySelectorAll('button')).find((b) => b.textContent!.trim() === label) as HTMLButtonElement;
  const listRequest = () => http.expectOne((r) => r.url === `${API_URL}/categories` && r.method === 'GET');

  async function settle() {
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  async function open(categories: Category[] = [network, legacy, hardware]) {
    localStorage.clear();
    await TestBed.configureTestingModule({
      imports: [Categories],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: MATERIAL_ANIMATIONS, useValue: { animationsDisabled: true } },
      ],
    }).compileComponents();

    http = TestBed.inject(HttpTestingController);
    dialog = TestBed.inject(MatDialog);
    snack = vi.spyOn(TestBed.inject(MatSnackBar), 'open');
    fixture = TestBed.createComponent(Categories);
    await settle();

    listRequest().flush(categories);
    await settle();
  }

  const dialogReturns = (value: unknown) =>
    vi.spyOn(dialog, 'open').mockReturnValue({ afterClosed: () => of(value) } as never);

  describe('the list', () => {
    it('asks for hidden categories too, since an Admin needs to see them', async () => {
      localStorage.clear();
      await TestBed.configureTestingModule({
        imports: [Categories],
        providers: [provideHttpClient(), provideHttpClientTesting()],
      }).compileComponents();
      http = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(Categories);
      await settle();

      expect(listRequest().request.params.get('includeInactive')).toBe('true');
    });

    it('shows the shown ones first, A to Z, then the hidden ones', async () => {
      await open();

      expect(rows().map((r) => r.querySelector('.name')!.textContent!.trim())).toEqual(['Hardware', 'Network', 'Archive']);
    });

    it('says in words whether each is shown or hidden', async () => {
      await open();

      expect(rowFor('Network').textContent).toContain('Shown');
      expect(rowFor('Archive').textContent).toContain('Hidden');
    });

    it('shows the description, or a dash when there is none', async () => {
      await open();

      expect(rowFor('Network').textContent).toContain('Wi-Fi and VPN');
      expect(rowFor('Hardware').textContent).toContain('—');
    });

    it('says how many are hidden', async () => {
      await open();

      expect(text()).toContain('1 hidden category is not offered for new incidents.');
    });

    it('says nothing about hidden ones when there are none', async () => {
      await open([network, hardware]);

      expect(text()).not.toContain('hidden category');
    });

    it('explains that hiding keeps old incidents intact, and that nothing is deleted', async () => {
      await open();

      expect(text()).toContain('incidents that already have it keep it');
      expect(text()).toContain('never deleted');
    });

    it('invites the first category when there are none', async () => {
      await open([]);

      expect(text()).toContain('There are no categories yet.');
    });

    it('says so, and recovers, when the categories cannot be loaded', async () => {
      localStorage.clear();
      await TestBed.configureTestingModule({
        imports: [Categories],
        providers: [provideHttpClient(), provideHttpClientTesting()],
      }).compileComponents();
      http = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(Categories);
      await settle();

      listRequest().flush({}, { status: 500, statusText: 'Server Error' });
      await settle();
      expect(text()).toContain('The categories could not be loaded.');

      Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('Try again'))!.click();
      await settle();
      listRequest().flush([network]);
      await settle();

      expect(rows().length).toBe(1);
    });
  });

  describe('creating', () => {
    it('adds the new category to the table, and says so', async () => {
      await open();
      dialogReturns({ id: 'c-x', name: 'Access', description: null, isActive: true });

      Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('New category'))!.click();
      await settle();

      expect(rowFor('Access')).toBeDefined();
      expect(snack.mock.calls[0][0]).toBe('The category "Access" was created.');
    });

    it('changes nothing when cancelled', async () => {
      await open();
      dialogReturns(undefined);

      Array.from(page().querySelectorAll('button')).find((b) => b.textContent!.includes('New category'))!.click();
      await settle();

      expect(rows().length).toBe(3);
      expect(snack).not.toHaveBeenCalled();
    });
  });

  describe('editing', () => {
    it('opens for that category, and shows the saved changes', async () => {
      await open();
      const open$ = dialogReturns({ ...network, name: 'Networking' });

      buttonIn(rowFor('Network'), 'Edit').click();
      await settle();

      expect(open$.mock.calls[0][1]).toMatchObject({ data: { category: network } });
      expect(rowFor('Networking')).toBeDefined();
      expect(snack.mock.calls[0][0]).toBe('The category "Networking" was updated.');
    });

    it('changes nothing when cancelled', async () => {
      await open();
      dialogReturns(undefined);

      buttonIn(rowFor('Network'), 'Edit').click();
      await settle();

      expect(snack).not.toHaveBeenCalled();
    });
  });

  describe('hiding and showing', () => {
    it('hides a category without asking, since it is always undoable, and says old incidents keep it', async () => {
      await open();

      buttonIn(rowFor('Network'), 'Hide').click();
      await settle();

      const request = http.expectOne(`${API_URL}/categories/c-n`);
      expect(request.request.method).toBe('PUT');
      expect(request.request.body).toEqual({ name: 'Network', description: 'Wi-Fi and VPN', isActive: false });
      request.flush({ ...network, isActive: false });
      await settle();

      expect(rowFor('Network').textContent).toContain('Hidden');
      expect(snack.mock.calls[0][0]).toContain('Existing incidents keep it');
    });

    it('moves a hidden category to the end of the list, with its button now saying Show', async () => {
      await open();

      buttonIn(rowFor('Network'), 'Hide').click();
      await settle();
      http.expectOne(`${API_URL}/categories/c-n`).flush({ ...network, isActive: false });
      await settle();

      const order = rows().map((r) => r.querySelector('.name')!.textContent!.trim());
      expect(order.indexOf('Hardware')).toBeLessThan(order.indexOf('Network'));
      expect(buttonIn(rowFor('Network'), 'Show')).toBeDefined();
    });

    it('shows a hidden category again', async () => {
      await open();

      buttonIn(rowFor('Archive'), 'Show').click();
      await settle();

      const request = http.expectOne(`${API_URL}/categories/c-l`);
      expect(request.request.body).toMatchObject({ name: 'Archive', isActive: true });
      request.flush({ ...legacy, isActive: true });
      await settle();

      expect(rowFor('Archive').textContent).toContain('Shown');
      expect(snack.mock.calls[0][0]).toContain('can be chosen for new incidents again');
    });

    it('cannot be clicked again while the change is saving', async () => {
      await open();

      buttonIn(rowFor('Network'), 'Hide').click();
      await settle();

      expect(buttonIn(rowFor('Hardware'), 'Hide').disabled).toBe(true);
      expect(buttonIn(rowFor('Network'), 'Edit').disabled).toBe(true);
      http.expectOne(`${API_URL}/categories/c-n`).flush({ ...network, isActive: false });
    });

    it('explains a refusal and reloads, because the screen may be out of date', async () => {
      await open();

      buttonIn(rowFor('Network'), 'Hide').click();
      await settle();
      http.expectOne(`${API_URL}/categories/c-n`).flush(
        { errors: { Name: ['A category with this name already exists.'] } },
        { status: 400, statusText: 'Bad Request' },
      );
      await settle();

      expect(snack.mock.calls[0][0]).toBe('A category with this name already exists.');
      listRequest().flush([network, hardware, legacy]); // a fresh load
    });
  });
});
