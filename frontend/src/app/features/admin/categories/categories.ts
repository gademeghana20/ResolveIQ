import { Component, DestroyRef, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatTableModule } from '@angular/material/table';

import { fieldErrors } from '../../../core/api-error';
import { Category } from '../../../core/category.models';
import { Badge } from '../../../shared/badge/badge';
import { AdminService } from '../admin.service';
import { CategoryDialog, CategoryDialogData } from './category-dialog';

type LoadState = 'loading' | 'ready' | 'failed';

@Component({
  selector: 'app-categories',
  imports: [MatTableModule, MatButtonModule, MatProgressBarModule, Badge],
  templateUrl: './categories.html',
  styleUrl: './categories.css',
})
export class Categories {
  private readonly admin = inject(AdminService);
  private readonly dialog = inject(MatDialog);
  private readonly snackBar = inject(MatSnackBar);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly columns = ['name', 'description', 'status', 'actions'];

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
