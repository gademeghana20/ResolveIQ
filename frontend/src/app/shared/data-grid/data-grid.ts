import { Component, ViewEncapsulation, computed, effect, input, output, signal } from '@angular/core';
import { AgGridAngular } from 'ag-grid-angular';
import { ColDef, GetRowIdParams, GridApi, GridReadyEvent, RowClickedEvent, SortChangedEvent } from 'ag-grid-community';

import { registerGridModules } from './grid-modules';
import { resolveIqGridTheme } from './grid-theme';

registerGridModules();

/** Which column the table is sorted by, and which way. */
export interface GridSort {
  column: string;
  direction: 'asc' | 'desc';
}

/**
 * The one table the whole app uses. It wraps AG Grid with ResolveIQ's look and sensible defaults:
 * it grows to fit its rows (no little scroll box inside the page), keeps columns in place, and leaves
 * "nothing to show" messages to the page that owns it.
 *
 * Sorting is reported, not performed: when someone clicks a column header the grid says which column
 * and direction, and the page decides what to do (usually ask the API for the rows in that order).
 */
@Component({
  selector: 'app-data-grid',
  imports: [AgGridAngular],
  encapsulation: ViewEncapsulation.None, // the grid builds its cells itself, so its styles must reach them
  template: `
    <div class="data-grid" role="region" [attr.aria-label]="ariaLabel()">
      <ag-grid-angular
        [theme]="theme"
        [rowData]="rows()"
        [columnDefs]="columns()"
        [defaultColDef]="defaultColDef"
        [context]="context()"
        [getRowId]="getRowId()"
        [rowClass]="rowClass()"
        [rowHeight]="rowHeight()"
        [headerHeight]="48"
        [sortingOrder]="sortingOrder"
        domLayout="autoHeight"
        [suppressColumnVirtualisation]="true"
        [suppressMovableColumns]="true"
        [suppressNoRowsOverlay]="true"
        [animateRows]="false"
        [enableCellTextSelection]="true"
        [ensureDomOrder]="true"
        (gridReady)="onGridReady($event)"
        (sortChanged)="onSortChanged($event)"
        (rowClicked)="onRowClicked($event)"
      />
    </div>
  `,
  styleUrl: './data-grid.css',
})
export class DataGrid<TRow = unknown> {
  /** The rows to show. */
  readonly rows = input.required<TRow[]>();

  /** The columns: their headings, where each value comes from, and how each cell is drawn. */
  readonly columns = input.required<ColDef<TRow>[]>();

  /** Names the table for screen readers, for example "Incidents". */
  readonly ariaLabel = input.required<string>();

  /** What uniquely identifies a row, so a changed row is updated in place instead of the whole table redrawn. */
  readonly rowId = input<((row: TRow) => string) | undefined>(undefined);

  /** The sort to show on the headers (the page keeps this up to date). */
  readonly sort = input<GridSort | null>(null);

  /** Things that cell renderers may call back into (such as "edit this row"). */
  readonly context = input<unknown>(undefined);

  readonly rowHeight = input(56);

  /** Gives rows a pointer cursor, for tables where clicking a row opens it. */
  readonly clickableRows = input(false);

  /** Fires when someone clicks a sortable column heading. */
  readonly sortChange = output<GridSort>();

  /** Fires when a row is clicked. */
  readonly rowClick = output<TRow>();

  protected readonly theme = resolveIqGridTheme;

  // Clicking a heading cycles ascending, descending. It never leaves the table unsorted.
  protected readonly sortingOrder: ('asc' | 'desc')[] = ['asc', 'desc'];

  protected readonly defaultColDef: ColDef = {
    sortable: false,
    resizable: false,
    suppressMovable: true,
    suppressHeaderMenuButton: true,
    wrapHeaderText: false,
  };

  protected readonly getRowId = computed(() => {
    const identify = this.rowId();
    return identify ? (params: GetRowIdParams<TRow>) => identify(params.data) : undefined;
  });

  protected readonly rowClass = computed(() => (this.clickableRows() ? 'clickable-row' : undefined));

  private readonly api = signal<GridApi<TRow> | null>(null);

  constructor() {
    // Keep the arrows on the headings matching the sort the page is using.
    effect(() => {
      const api = this.api();
      const sort = this.sort();

      if (api) {
        api.applyColumnState({
          state: sort ? [{ colId: sort.column, sort: sort.direction }] : [],
          defaultState: { sort: null },
        });
      }
    });
  }

  protected onGridReady(event: GridReadyEvent<TRow>): void {
    this.api.set(event.api);
  }

  protected onSortChanged(event: SortChangedEvent<TRow>): void {
    // The grid also announces sorts that we applied ourselves; only a person's click is news.
    if (event.source === 'api') {
      return;
    }

    const sorted = event.api.getColumnState().find((column) => column.sort);
    if (sorted?.sort) {
      this.sortChange.emit({ column: sorted.colId, direction: sorted.sort });
    }
  }

  protected onRowClicked(event: RowClickedEvent<TRow>): void {
    if (event.data) {
      this.rowClick.emit(event.data);
    }
  }
}
