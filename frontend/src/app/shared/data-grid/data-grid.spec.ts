import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ColDef } from 'ag-grid-community';

import { DataGrid, GridSort } from './data-grid';
import { clickHeading, drawGrid, gridHeadings, gridRows, sortOf } from './grid-testing';

interface Fruit {
  id: string;
  name: string;
  count: number;
}

@Component({
  imports: [DataGrid],
  template: `
    <app-data-grid
      ariaLabel="Fruit"
      [rows]="rows()"
      [columns]="columns"
      [rowId]="rowId"
      [sort]="sort()"
      [clickableRows]="true"
      (sortChange)="sorts.push($event)"
      (rowClick)="clicks.push($event)"
    />
  `,
})
class Host {
  readonly rows = signal<Fruit[]>([
    { id: 'a', name: 'Apple', count: 3 },
    { id: 'b', name: 'Banana', count: 12 },
  ]);
  readonly sort = signal<GridSort | null>({ column: 'name', direction: 'asc' });
  readonly sorts: GridSort[] = [];
  readonly clicks: Fruit[] = [];
  readonly rowId = (fruit: Fruit) => fruit.id;
  readonly columns: ColDef<Fruit>[] = [
    { colId: 'name', headerName: 'Name', field: 'name', sortable: true, comparator: () => 0 },
    { colId: 'count', headerName: 'How many', field: 'count', sortable: true, comparator: () => 0 },
  ];
}

describe('DataGrid', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;

  const root = () => fixture.nativeElement as HTMLElement;
  const draw = async () => {
    fixture.detectChanges();
    await fixture.whenStable();
    await drawGrid(() => fixture.detectChanges());
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();
    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    await draw();
  });

  it('shows the headings and the rows', () => {
    expect(gridHeadings(root())).toEqual(['Name', 'How many']);
    expect(gridRows(root())).toEqual([
      ['Apple', '3'],
      ['Banana', '12'],
    ]);
  });

  it('is a labelled region, so screen readers can name the table', () => {
    expect(root().querySelector('[role="region"]')!.getAttribute('aria-label')).toBe('Fruit');
  });

  it('shows the sort the page asked for, without reporting it back as a click', () => {
    expect(sortOf(root(), 'Name')).toBe('ascending');
    expect(sortOf(root(), 'How many')).toBe('none');
    expect(host.sorts).toEqual([]);
  });

  it('follows the page when it changes the sort', async () => {
    host.sort.set({ column: 'count', direction: 'desc' });
    await draw();

    expect(sortOf(root(), 'How many')).toBe('descending');
    expect(sortOf(root(), 'Name')).toBe('none');
    expect(host.sorts).toEqual([]);
  });

  it('reports which column a person clicked, and leaves the sorting to the page', async () => {
    clickHeading(root(), 'How many');
    await draw();

    expect(host.sorts).toEqual([{ column: 'count', direction: 'asc' }]);
    expect(gridRows(root())[0][0]).toBe('Apple'); // the grid did not reorder anything itself
  });

  it('reports the row that was clicked', async () => {
    root().querySelector<HTMLElement>('.ag-cell[col-id="name"]')!.click();
    await draw();

    expect(host.clicks).toEqual([{ id: 'a', name: 'Apple', count: 3 }]);
  });

  it('updates a row in place when its data changes', async () => {
    host.rows.set([
      { id: 'a', name: 'Apple', count: 4 },
      { id: 'b', name: 'Banana', count: 12 },
    ]);
    await draw();

    expect(gridRows(root())[0]).toEqual(['Apple', '4']);
  });
});
