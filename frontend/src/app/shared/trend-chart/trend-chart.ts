import { Component, computed, input, signal } from '@angular/core';

import { formatDay, niceAxis } from '../format';

export interface TrendPoint {
  date: string; // "2026-11-17"
  value: number;
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

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly plotLeft = MARGIN.left;
  protected readonly plotRight = WIDTH - MARGIN.right;
  protected readonly plotTop = MARGIN.top;
  protected readonly plotBottom = HEIGHT - MARGIN.bottom;

  protected readonly showTable = signal(false);
  protected readonly hoverIndex = signal<number | null>(null);

  protected readonly dates = computed(() => this.series()[0]?.points.map((p) => p.date) ?? []);

  private readonly axis = computed(() =>
    niceAxis(Math.max(0, ...this.series().flatMap((s) => s.points.map((p) => p.value)))),
  );

  protected readonly yTicks = computed(() => {
    const { top, step } = this.axis();
    const ticks: { value: number; y: number }[] = [];
    for (let value = 0; value <= top; value += step) {
      ticks.push({ value, y: this.y(value) });
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
      const coordinates = s.points.map((p, index) => `${this.x(index).toFixed(1)},${this.y(p.value).toFixed(1)}`);
      const last = s.points[s.points.length - 1];

      return {
        name: s.name,
        color: s.color,
        path: coordinates.length ? `M${coordinates.join(' L')}` : '',
        endX: last ? this.x(s.points.length - 1) : 0,
        endY: last ? this.y(last.value) : 0,
        hasEnd: !!last,
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
      values: this.series().map((s) => ({
        name: s.name,
        color: s.color,
        value: s.points[index]?.value ?? 0,
        y: this.y(s.points[index]?.value ?? 0),
      })),
    };
  });

  protected readonly tableRows = computed(() =>
    this.dates().map((date, index) => ({
      date: formatDay(date),
      values: this.series().map((s) => s.points[index]?.value ?? 0),
    })),
  );

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
