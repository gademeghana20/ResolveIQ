import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { ColDef } from 'ag-grid-community';

import { fieldErrors } from '../../../core/api-error';
import { Category } from '../../../core/category.models';
import { ActionsCell, BadgeCell, RowAction } from '../../../shared/data-grid/cell-renderers';
import { DataGrid } from '../../../shared/data-grid/data-grid';
import { AdminService } from '../admin.service';
import { CategoryDialog, CategoryDialogData } from './category-dialog';

type LoadState = 'loading' | 'ready' | 'failed';

@Component({
  selector: 'app-categories',
  imports: [MatButtonModule, MatProgressBarModule, DataGrid],
  templateUrl: './categories.html',
  styleUrl: './categories.css',
})
export class Categories {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly rowId = (category: Category) => category.id;

  protected readonly columnDefs: ColDef<Category>[] = [
    {
      colId: 'name',
      headerName: 'Name',
      field: 'name',
      flex: 1.2,
      minWidth: 160,
      cellClass: 'strong',
      // A hidden category reads as set aside, though its status badge says so in words too.
      cellClassRules: { muted: (p) => !p.data?.isActive },
    },
    {
      colId: 'description',
      headerName: 'Description',
      flex: 2.5,
      minWidth: 220,
      valueGetter: (p) => p.data?.description ?? '—',
      cellClass: 'muted',
    },
    {
      colId: 'status',
      headerName: 'Status',
      width: 130,
      cellRenderer: BadgeCell,
      cellRendererParams: {
        label: (c: Category) => (c.isActive ? 'Shown' : 'Hidden'),
        tone: (c: Category) => (c.isActive ? 'success' : 'neutral'),
      },
    },
    {
      colId: 'actions',
      headerName: 'Actions',
      width: 170,
      cellRenderer: ActionsCell,
      cellRendererParams: {
        mode: 'buttons',
        actions: (c: Category): RowAction<Category>[] => [
          { label: 'Edit', ariaLabel: 'Edit ' + c.name, run: (row) => this.guarded(() => this.edit(row)) },
          {
            label: c.isActive ? 'Hide' : 'Show',
            ariaLabel: (c.isActive ? 'Hide ' : 'Show ') + c.name,
            run: (row) => this.guarded(() => this.toggleVisible(row)),
          },
        ],
      },
    },
  ];

  /** The table's buttons are not disabled while a change is in flight, so a second click is ignored here instead. */
  private guarded(action: () => void): void {
    if (!this.busy()) {
      action();
    }
  }

  protected readonly state = signal<LoadState>('loading');
  protected readonly categories = signal<Category[]>([]);
  protected readonly busy = signal(false);

  // Shown names first, hidden ones after, each group A to Z.
  protected readonly sorted = computed(() =>
    [...this.categories()].sort(
      (a, b) => Number(b.isActive) - Number(a.isActive) || a.name.localeCompare(b.name, undefined, { sensitivity: 'base' }),
    ),
  );

  protected readonly hiddenCount = computed(() => this.categories().filter((c) => !c.isActive).length);

  constructor() {
    this.load();
  }

  protected load(): void {
    this.state.set('loading');

    this.admin
      .getCategories()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (categories) => {
          this.categories.set(categories);
          this.state.set('ready');
        },
        error: () => this.state.set('failed'),
      });
  }

  protected create(): void {
    this.dialog
      .open<CategoryDialog, CategoryDialogData, Category>(CategoryDialog, { data: {} })
      .afterClosed()
      .subscribe((created) => {
        if (created) {
          this.categories.update((list) => [...list, created]);
          this.tell(`The category "${created.name}" was created.`);
        }
      });
  }

  protected edit(category: Category): void {
    this.dialog
      .open<CategoryDialog, CategoryDialogData, Category>(CategoryDialog, { data: { category } })
      .afterClosed()
      .subscribe((saved) => {
        if (saved) {
          this.replace(saved);
          this.tell(`The category "${saved.name}" was updated.`);
        }
      });
  }

  /**
   * Hides or shows a category. Hiding only stops it being offered for new incidents:
   * incidents that already have it keep it. Nothing is ever deleted, so this is always safe to undo.
   */
  protected toggleVisible(category: Category): void {
    const nowActive = !category.isActive;
    this.busy.set(true);

    this.admin
      .updateCategory(category.id, { name: category.name, description: category.description, isActive: nowActive })
      .subscribe({
        next: (saved) => {
          this.busy.set(false);
          this.replace(saved);
          this.tell(
            nowActive
              ? `"${saved.name}" can be chosen for new incidents again.`
              : `"${saved.name}" is hidden from new incidents. Existing incidents keep it.`,
          );
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.tell(Object.values(fieldErrors(error) ?? {})[0] ?? 'That could not be done. Please try again.');
          this.load(); // what is on screen may be out of date
        },
      });
  }

  private replace(saved: Category): void {
    this.categories.update((list) => list.map((c) => (c.id === saved.id ? saved : c)));
  }

  private tell(message: string): void {
    this.snackBar.open(message, 'Dismiss', { duration: 8000 });
  }
}
