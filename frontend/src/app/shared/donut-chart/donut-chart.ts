import { Component, computed, input, signal } from '@angular/core';

export interface DonutItem {
  label: string;
  value: number;
  /** A CSS colour, e.g. 'var(--viz-series-1)'. An item keeps its colour whatever else is shown. */
  color: string;
  /** Optional small print under the label in the legend, e.g. what a combined group is made of. */
  detail?: string;
}

// The ring is drawn in a 200 x 200 square that scales to the card.
const CENTER = 100;
const RADIUS = 70;
const THICKNESS = 26;
const CIRCUMFERENCE = 2 * Math.PI * RADIUS;

// A small gap of the card's own colour between slices, so neighbours never merge (even in the same hue family).
const GAP = 3;

/**
 * A donut: one whole split into a few parts. It is only the picture; the numbers never depend on it:
 *  - every part is also written out in a legend with its count and share,
 *  - hovering a slice (or a legend row), or using the arrow keys when the ring has focus, shows that part in the
 *    middle of the ring,
 *  - a "View as table" switch lists everything as plain text.
 * Keep it to a handful of parts (about six at most); past that, ranked bars read better.
 */
@Component({
  selector: 'app-donut-chart',
  templateUrl: './donut-chart.html',
  styleUrl: './donut-chart.css',
})
export class DonutChart {
  readonly items = input.required<DonutItem[]>();
  readonly ariaLabel = input.required<string>();
  /** The word under the total in the middle of the ring, e.g. "incidents". */
  readonly centerLabel = input('total');
  readonly emptyText = input('There are no incidents yet.');

  protected readonly showTable = signal(false);
  protected readonly activeIndex = signal<number | null>(null);

  protected readonly center = CENTER;
  protected readonly radius = RADIUS;
  protected readonly thickness = THICKNESS;

  protected readonly total = computed(() => this.items().reduce((sum, item) => sum + item.value, 0));

  /** Every part with its share, in the order given (the legend and the table use the same list). */
  protected readonly parts = computed(() => {
    const total = this.total();
    return this.items().map((item, index) => ({
      ...item,
      index,
      percent: total === 0 ? 0 : Math.round((100 * item.value) / total),
    }));
  });

  /** The ring: one arc per part that has something in it. */
  protected readonly slices = computed(() => {
    const total = this.total();
    const visible = this.parts().filter((part) => part.value > 0);
    const gap = visible.length > 1 ? GAP : 0;
    let offset = 0;

    return visible.map((part) => {
      const length = (part.value / total) * CIRCUMFERENCE;
      const slice = {
        index: part.index,
        color: part.color,
        label: part.label,
        text: `${part.label}: ${part.value} (${part.percent}%)`,
        dash: `${Math.max(0.5, length - gap)} ${CIRCUMFERENCE}`,
        offset: -offset,
      };
      offset += length;
      return slice;
    });
  });

  /** What the middle of the ring says: the whole, or the part being pointed at. */
  protected readonly readout = computed(() => {
    const index = this.activeIndex();
    const part = index === null ? undefined : this.parts()[index];

    return part
      ? { big: String(part.value), small: `${part.label} · ${part.percent}%` }
      : { big: String(this.total()), small: this.centerLabel() };
  });

  protected setActive(index: number | null): void {
    this.activeIndex.set(index);
  }

  protected onFocus(): void {
    if (this.activeIndex() === null && this.slices().length) {
      this.activeIndex.set(this.slices()[0].index);
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    const slices = this.slices();
    if (slices.length === 0) {
      return;
    }

    if (event.key === 'Escape') {
      this.activeIndex.set(null);
      return;
    }

    // Arrow keys step through the parts that are on the ring, wrapping round.
    const position = slices.findIndex((slice) => slice.index === this.activeIndex());
    const step = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : event.key === 'ArrowLeft' || event.key === 'ArrowUp' ? -1 : 0;

    if (step !== 0) {
      event.preventDefault();
      const next = position === -1 ? 0 : (position + step + slices.length) % slices.length;
      this.activeIndex.set(slices[next].index);
    } else if (event.key === 'Home' || event.key === 'End') {
      event.preventDefault();
      this.activeIndex.set(slices[event.key === 'Home' ? 0 : slices.length - 1].index);
    }
  }

  protected toggleTable(): void {
    this.showTable.update((shown) => !shown);
  }
}
