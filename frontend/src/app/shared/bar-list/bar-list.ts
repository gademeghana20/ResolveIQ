import { Component, computed, input } from '@angular/core';

const BAR_SHARE_OF_ROW = 80;

export interface BarItem {
  label: string;
  value: number;
  /** A CSS colour for this bar. Leave out to use the standard single series colour. */
  color?: string;
}

/**
 * Horizontal bars, one row per item: a name, a bar sized by its value, and the value at the bar's tip.
 * Every value is also written out as text, so nothing depends on seeing the bar or its colour.
 * One colour for all bars unless an item says otherwise (colour is only for meaning, such as status).
 */
@Component({
  selector: 'app-bar-list',
  templateUrl: './bar-list.html',
  styleUrl: './bar-list.css',
})
export class BarList {
  readonly items = input.required<BarItem[]>();
  readonly ariaLabel = input.required<string>();
  readonly emptyText = input('Nothing to show for this period.');

  protected readonly hasData = computed(() => this.items().some((item) => item.value > 0));

  protected readonly rows = computed(() => {
    const largest = Math.max(1, ...this.items().map((item) => item.value));

    // The longest bar fills 80% of the row, leaving room for its value beside it. Bars stay proportional.
    return this.items().map((item) => ({ ...item, percent: (item.value / largest) * BAR_SHARE_OF_ROW }));
  });
}
