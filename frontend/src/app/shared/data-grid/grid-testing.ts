/**
 * Helpers for tests that look inside a table. AG Grid draws its rows and cells itself, a moment after the
 * data arrives, so tests wait for it and then read what is on screen (text, headings), as a person would.
 */

import { vi } from 'vitest';

// The first table in a test file has to load all of AG Grid, which can take longer than the default 5 seconds
// on a busy computer. A longer limit stops those tests failing for being slow rather than wrong.
vi.setConfig({ testTimeout: 30_000, hookTimeout: 30_000 });

/** Gives the grid time to draw. Call after the data has been set and the fixture has settled. */
export async function drawGrid(detect: () => void): Promise<void> {
  // The grid draws in several small steps (rows, then each cell, then custom cell contents), so keep
  // looking until a round passes with nothing new on screen.
  let previous = -1;
  for (let round = 0; round < 40; round++) {
    await new Promise((resolve) => setTimeout(resolve, 20));
    detect();
    const size = document.querySelectorAll('.ag-cell, .ag-header-cell, app-badge').length;
    if (size === previous && round > 8) {
      return;
    }
    previous = size;
  }
}

/** The text of every cell, row by row. */
export function gridRows(root: HTMLElement): string[][] {
  return Array.from(root.querySelectorAll('.ag-row')).map((row) =>
    Array.from(row.querySelectorAll('.ag-cell')).map((cell) => (cell.textContent ?? '').replace(/\s+/g, ' ').trim()),
  );
}

/** The column headings, in order. */
export function gridHeadings(root: HTMLElement): string[] {
  return Array.from(root.querySelectorAll('.ag-header-cell')).map((cell) =>
    (cell.querySelector('.ag-header-cell-text')?.textContent ?? '').trim(),
  );
}

/** The heading element with this text. */
export function gridHeading(root: HTMLElement, name: string): HTMLElement {
  const heading = Array.from(root.querySelectorAll<HTMLElement>('.ag-header-cell')).find(
    (cell) => cell.querySelector('.ag-header-cell-text')?.textContent?.trim() === name,
  );
  if (!heading) {
    throw new Error(`No column heading "${name}"`);
  }
  return heading;
}

/** Which way a heading is sorted, as the grid tells assistive technology. */
export function sortOf(root: HTMLElement, name: string): string | null {
  return gridHeading(root, name).getAttribute('aria-sort');
}

/** Clicks a heading, as a person would to sort. */
export function clickHeading(root: HTMLElement, name: string): void {
  const heading = gridHeading(root, name);
  (heading.querySelector<HTMLElement>('.ag-header-cell-label') ?? heading).click();
}

/** A real row element, for clicking. */
export function gridRow(root: HTMLElement, index = 0): HTMLElement {
  return root.querySelectorAll<HTMLElement>('.ag-row')[index];
}

