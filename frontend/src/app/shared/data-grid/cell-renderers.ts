import { DatePipe } from '@angular/common';
import { Component } from '@angular/core';
import { ICellRendererAngularComp } from 'ag-grid-angular';
import { ICellRendererParams } from 'ag-grid-community';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatMenuModule } from '@angular/material/menu';
import { RouterLink } from '@angular/router';

import { Badge, BadgeTone } from '../badge/badge';

/** What a column hands to BadgeCell: how to turn the row into badge text and colour. */
export interface BadgeCellParams<TRow = unknown> extends ICellRendererParams<TRow> {
  label: (row: TRow) => string;
  tone: (row: TRow) => BadgeTone;
}

/** A coloured badge in a table cell. */
@Component({
  selector: 'app-badge-cell',
  imports: [Badge],
  template: `<app-badge [label]="label" [tone]="tone" />`,
})
export class BadgeCell implements ICellRendererAngularComp {
  protected label = '';
  protected tone: BadgeTone = 'neutral';

  agInit(params: BadgeCellParams): void {
    this.refresh(params);
  }

  refresh(params: BadgeCellParams): boolean {
    if (params.data) {
      this.label = params.label(params.data);
      this.tone = params.tone(params.data);
    }
    return true;
  }
}

/** What a column hands to LinkCell: where the link goes. */
export interface LinkCellParams<TRow = unknown> extends ICellRendererParams<TRow> {
  link: (row: TRow) => string[];
}

/** The cell's text as a real link (so it can be opened in a new tab and reached by keyboard). */
@Component({
  selector: 'app-link-cell',
  imports: [RouterLink],
  template: `<a class="cell-link" [routerLink]="link" (click)="$event.stopPropagation()">{{ text }}</a>`,
})
export class LinkCell implements ICellRendererAngularComp {
  protected text = '';
  protected link: string[] = [];

  agInit(params: LinkCellParams): void {
    this.refresh(params);
  }

  refresh(params: LinkCellParams): boolean {
    if (params.data) {
      this.text = String(params.value ?? '');
      this.link = params.link(params.data);
    }
    return true;
  }
}

/** What a column hands to BadgeNoteCell: the badge, plus an optional small note beside it. */
export interface BadgeNoteCellParams<TRow = unknown> extends BadgeCellParams<TRow> {
  /** A date to show as "due …" beside the badge, or null for none. */
  dueAt: (row: TRow) => string | null;
}

/** A badge with its due date directly underneath it. */
@Component({
  selector: 'app-badge-note-cell',
  imports: [Badge, DatePipe],
  template: `
    <span class="badge-with-note">
      <app-badge [label]="label" [tone]="tone" />
      @if (dueAt) {
        <span class="badge-note">due {{ dueAt | date: 'MMM d, h:mm a' }}</span>
      }
    </span>
  `,
})
export class BadgeNoteCell implements ICellRendererAngularComp {
  protected label = '';
  protected tone: BadgeTone = 'neutral';
  protected dueAt: string | null = null;

  agInit(params: BadgeNoteCellParams): void {
    this.refresh(params);
  }

  refresh(params: BadgeNoteCellParams): boolean {
    if (params.data) {
      this.label = params.label(params.data);
      this.tone = params.tone(params.data);
      this.dueAt = params.dueAt(params.data);
    }
    return true;
  }
}

/** One thing a person can do to a row: "Edit", "Hide", "Reset password…". */
export interface RowAction<TRow = unknown> {
  label: string;
  /** For screen readers, which row this belongs to, for example "Edit Hardware". */
  ariaLabel?: string;
  run: (row: TRow) => void;
}

/** What a column hands to ActionsCell. */
export interface ActionsCellParams<TRow = unknown> extends ICellRendererParams<TRow> {
  /** The actions for this row. An empty list shows a dash (with `emptyHint` as its tooltip). */
  actions: (row: TRow) => RowAction<TRow>[];
  /** 'buttons' shows each action as a button; 'menu' tucks them into a "⋮" menu. */
  mode: 'buttons' | 'menu';
  /** Describes the menu button for screen readers: "Actions for dana@example.com". */
  menuLabel?: (row: TRow) => string;
  emptyHint?: string;
}

/** Row actions inside a table cell. */
@Component({
  selector: 'app-actions-cell',
  imports: [MatButtonModule, MatIconModule, MatMenuModule],
  template: `
    @if (items.length === 0) {
      <span class="muted" [attr.title]="emptyHint">—</span>
    } @else if (mode === 'menu') {
      <button mat-icon-button type="button" [matMenuTriggerFor]="menu" [attr.aria-label]="menuLabel">
        <mat-icon>more_vert</mat-icon>
      </button>
      <mat-menu #menu="matMenu">
        @for (item of items; track item.label) {
          <button mat-menu-item type="button" (click)="item.run(row!)">{{ item.label }}</button>
        }
      </mat-menu>
    } @else {
      <span class="cell-actions">
        @for (item of items; track item.label) {
          <button mat-button type="button" [attr.aria-label]="item.ariaLabel ?? item.label" (click)="item.run(row!)">
            {{ item.label }}
          </button>
        }
      </span>
    }
  `,
})
export class ActionsCell implements ICellRendererAngularComp {
  protected row: unknown;
  protected items: RowAction[] = [];
  protected mode: 'buttons' | 'menu' = 'buttons';
  protected menuLabel = 'Actions';
  protected emptyHint: string | undefined;

  agInit(params: ActionsCellParams): void {
    this.refresh(params);
  }

  refresh(params: ActionsCellParams): boolean {
    this.row = params.data;
    this.mode = params.mode;
    this.emptyHint = params.emptyHint;
    if (params.data) {
      this.items = params.actions(params.data);
      this.menuLabel = params.menuLabel?.(params.data) ?? 'Actions';
    }
    return true;
  }
}
