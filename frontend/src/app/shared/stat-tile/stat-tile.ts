import { NgTemplateOutlet } from '@angular/common';
import { Component, input } from '@angular/core';
import { RouterLink } from '@angular/router';

/**
 * One headline number with a label. When it is given a link it becomes a real link (so the keyboard
 * and screen readers can use it), for example "Breached: 3" that opens the list of breached incidents.
 * "hero" makes it the big lead figure; use it once per page.
 */
@Component({
  selector: 'app-stat-tile',
  imports: [NgTemplateOutlet, RouterLink],
  templateUrl: './stat-tile.html',
  styleUrl: './stat-tile.css',
})
export class StatTile {
  readonly label = input.required<string>();
  readonly value = input.required<string | number>();

  /** A smaller line under the value, e.g. "oldest waiting 3 h". */
  readonly note = input<string | null>(null);

  /** Where the tile leads, as router segments (['/incidents']). Leave out for a plain tile. */
  readonly link = input<string[] | null>(null);
  readonly queryParams = input<Record<string, string> | null>(null);

  readonly hero = input(false);
}
