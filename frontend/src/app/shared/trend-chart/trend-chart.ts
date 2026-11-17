import { Component, computed, input, signal } from '@angular/core';

import { formatDay, niceAxis } from '../format';

export interface TrendPoint {
  date: string; // "2026-11-17"
  /** Empty when there is nothing to plot that day: the line breaks there instead of dropping to zero. */
  value: number | null;
  /** Small print added to the tooltip and the table, e.g. "3 resolved". */
  note?: string;
}

export interface TrendSeries {
  name: string;
  /** A CSS colour, e.g. 'var(--viz-series-1)'. A series keeps its colour whatever else is shown. */
  color: string;
  points: TrendPoint[];
}

// The drawing is 640 x 240 "units" that scale to the width of the card.
const WIDTH = 640;
const HEIGHT = 240;
const MARGIN = { left: 40, right: 20, top: 12, bottom: 28 };
const PLOT_WIDTH = WIDTH - MARGIN.left - MARGIN.right;
const PLOT_HEIGHT = HEIGHT - MARGIN.top - MARGIN.bottom;

/**
 * Lines over time. Several things keep it readable for everyone:
 *  - a legend names each line (colour is never the only clue),
 *  - a crosshair and tooltip on hover, or with the arrow keys when the chart has focus,
 *  - a "View as table" switch, so every value can be read without a pointer or colour vision.
 */
@Component({
  selector: 'app-trend-chart',
  templateUrl: './trend-chart.html',
  styleUrl: './trend-chart.css',
})
export class TrendChart {
  readonly series = input.required<TrendSeries[]>();
  readonly ariaLabel = input.required<string>();
  /** A fixed top for the vertical axis (for example 100 for percentages). Leave out to fit the data. */
  readonly maxValue = input<number | null>(null);
  /** How a value is written on the axis, in the tooltip and in the table (for example 67 -> "67%"). */
  readonly valueFormat = input<(value: number) => string>((value) => String(value));

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly plotLeft = MARGIN.left;
  protected readonly plotRight = WIDTH - MARGIN.right;
  protected readonly plotTop = MARGIN.top;
  protected readonly plotBottom = HEIGHT - MARGIN.bottom;

  protected readonly showTable = signal(false);
  protected readonly hoverIndex = signal<number | null>(null);

  protected readonly dates = computed(() => this.series()[0]?.points.map((p) => p.date) ?? []);

  private readonly axis = computed(() => {
    const fixed = this.maxValue();
    return fixed !== null
      ? { top: fixed, step: fixed / 4 }
      : niceAxis(Math.max(0, ...this.series().flatMap((s) => s.points.map((p) => p.value ?? 0))));
  });

  protected readonly yTicks = computed(() => {
    const { top, step } = this.axis();
    const ticks: { value: number; y: number; text: string }[] = [];
    for (let value = 0; value <= top + 1e-9; value += step) {
      ticks.push({ value, y: this.y(value), text: this.valueFormat()(value) });
    }
    return ticks;
  });

  /** A few evenly spaced dates along the bottom, always including the first and last. */
  protected readonly xLabels = computed(() => {
    const count = this.dates().length;
    if (count === 0) {
      return [];
    }

    const every = Math.max(1, Math.ceil(count / 6));
    const wanted = new Set<number>([0, count - 1]);
    for (let i = 0; i < count; i += every) {
      // Skip a label that would crowd the last one.
      if (count - 1 - i >= every / 2 || i === 0) {
        wanted.add(i);
      }
    }

    return [...wanted].sort((a, b) => a - b).map((index) => ({ x: this.x(index), text: formatDay(this.dates()[index]) }));
  });

  protected readonly lines = computed(() =>
    this.series().map((s) => {
      // A run of days with data is one stretch of line; a day without data ends it.
      const stretches: { x: number; y: number }[][] = [];
      let current: { x: number; y: number }[] = [];

      s.points.forEach((point, index) => {
        if (point.value === null) {
          if (current.length) {
            stretches.push(current);
          }
          current = [];
        } else {
          current.push({ x: this.x(index), y: this.y(point.value) });
        }
      });
      if (current.length) {
        stretches.push(current);
      }

      const path = stretches
        .filter((stretch) => stretch.length > 1)
        .map((stretch) => `M${stretch.map((c) => `${c.x.toFixed(1)},${c.y.toFixed(1)}`).join(' L')}`)
        .join(' ');

      // A day with data between two days without has no line to sit on, so it is shown as a dot.
      const lone = stretches.filter((stretch) => stretch.length === 1).map((stretch) => stretch[0]);
      const last = stretches.length ? stretches[stretches.length - 1][stretches[stretches.length - 1].length - 1] : null;

      return {
        name: s.name,
        color: s.color,
        path,
        lone,
        endX: last?.x ?? 0,
        endY: last?.y ?? 0,
        hasEnd: last !== null,
      };
    }),
  );

  /** What the tooltip shows for the hovered date: each series' value on that day. */
  protected readonly hovered = computed(() => {
    const index = this.hoverIndex();
    if (index === null || index >= this.dates().length) {
      return null;
    }

    const xPosition = this.x(index);
    return {
      x: xPosition,
      percent: (xPosition / WIDTH) * 100,
      date: formatDay(this.dates()[index]),
      values: this.series().map((s) => {
        const point = s.points[index];
        const value = point ? point.value : 0;

        return {
          name: s.name,
          color: s.color,
          text: this.describe(value, point?.note),
          y: value === null ? null : this.y(value),
        };
      }),
    };
  });

  protected readonly tableRows = computed(() =>
    this.dates().map((date, index) => ({
      date: formatDay(date),
      values: this.series().map((s) => {
        const point = s.points[index];
        return point ? this.describe(point.value, point.note) : this.describe(0);
      }),
    })),
  );

  /** A value as text, with its note; "no data" for a day with nothing to plot. */
  private describe(value: number | null, note?: string): string {
    if (value === null) {
      return 'no data';
    }
    return note ? `${this.valueFormat()(value)} · ${note}` : this.valueFormat()(value);
  }

  protected x(index: number): number {
    const count = this.dates().length;
    return count <= 1 ? MARGIN.left + PLOT_WIDTH / 2 : MARGIN.left + (index * PLOT_WIDTH) / (count - 1);
  }

  protected y(value: number): number {
    return MARGIN.top + PLOT_HEIGHT - (value / this.axis().top) * PLOT_HEIGHT;
  }

  protected onPointerMove(event: PointerEvent): void {
    const count = this.dates().length;
    if (count === 0) {
      return;
    }

    const box = (event.currentTarget as SVGElement).getBoundingClientRect();
    const drawingX = ((event.clientX - box.left) / box.width) * WIDTH;
    const fraction = (drawingX - MARGIN.left) / PLOT_WIDTH;
    this.hoverIndex.set(Math.min(count - 1, Math.max(0, Math.round(fraction * (count - 1)))));
  }

  protected clearHover(): void {
    this.hoverIndex.set(null);
  }

  protected onFocus(): void {
    // Keyboard users land on the most recent day.
    if (this.hoverIndex() === null && this.dates().length) {
      this.hoverIndex.set(this.dates().length - 1);
    }
  }

  protected onKeydown(event: KeyboardEvent): void {
    const count = this.dates().length;
    const current = this.hoverIndex() ?? count - 1;

    const next =
      event.key === 'ArrowLeft'
        ? current - 1
        : event.key === 'ArrowRight'
          ? current + 1
          : event.key === 'Home'
            ? 0
            : event.key === 'End'
              ? count - 1
              : null;

    if (event.key === 'Escape') {
      this.clearHover();
    } else if (next !== null && count > 0) {
      event.preventDefault();
      this.hoverIndex.set(Math.min(count - 1, Math.max(0, next)));
    }
  }

  protected toggleTable(): void {
    this.showTable.update((shown) => !shown);
  }
}
