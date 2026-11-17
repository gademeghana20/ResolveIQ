import { Component, computed, input, output } from '@angular/core';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';

/** The page and page size the person has chosen. */
export interface PageChange {
  page: number; // starts at 1
  pageSize: number;
}

/**
 * The bar under a table: "Rows per page" on the left, "1–20 of 57" in the middle, page buttons on the right.
 * It is its own bar, set apart from the table, and it spreads across the full width.
 */
@Component({
  selector: 'app-grid-footer',
  imports: [MatButtonModule, MatIconModule, MatSelectModule],
  templateUrl: './grid-footer.html',
  styleUrl: './grid-footer.css',
})
export class GridFooter {
  readonly total = input.required<number>();
  readonly page = input.required<number>(); // starts at 1
  readonly pageSize = input.required<number>();
  readonly pageSizeOptions = input<number[]>([10, 20, 50]);
  readonly label = input('rows');

  readonly pageChange = output<PageChange>();

  protected readonly pageCount = computed(() => Math.max(1, Math.ceil(this.total() / this.pageSize())));
  protected readonly first = computed(() => (this.total() === 0 ? 0 : (this.page() - 1) * this.pageSize() + 1));
  protected readonly last = computed(() => Math.min(this.total(), this.page() * this.pageSize()));
  protected readonly atStart = computed(() => this.page() <= 1);
  protected readonly atEnd = computed(() => this.page() >= this.pageCount());

  protected go(page: number): void {
    this.pageChange.emit({ page: Math.min(Math.max(1, page), this.pageCount()), pageSize: this.pageSize() });
  }

  protected resize(pageSize: number): void {
    this.pageChange.emit({ page: 1, pageSize });
  }
}
